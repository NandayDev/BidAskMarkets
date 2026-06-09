using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace BidAskMarkets
{
    class Filter
    {
        private static readonly HttpClient httpClient = new();

        private const string XETRA = "xetra";
        private const string LS = "ls";
        private const string EIX = "eix";

        // Stato per ogni feed: Instrument -> (Second -> Entry)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<DateTime, Entry>> xetraState
            = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<DateTime, Entry>> lsState
            = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<DateTime, Entry>> eixState
            = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> etfIsins = ParseEtfIsins();

        private static readonly ConcurrentDictionary<string, Entry> xetraLastEntry = [];

        private const int year = 2026;
        private const int month = 6;
        private const int day = 8;

        private static readonly DateTime start = new(year, month, day, 11, 35, 0);
        private static readonly DateTime end = new(year, month, day, 15, 0, 0);

        public static async Task Run()
        {
            await DownloadEIX();
            return;

            DeleteDataFolder(XETRA);
            DeleteDataFolder(EIX);
            DeleteDataFolder(LS);

            Task xetraTask = Task.Run(() => DownloadXetra());

            Task eixTask = Task.Run(() => DownloadEIX());

            Task lsTask = Task.Run(() => DownloadLS());

            Task.WaitAll([xetraTask, eixTask, lsTask]);
        }

        private static bool DeleteDataFolder(string exchange)
        {
            string folderPath = GetPath(exchange);
            if (!Directory.Exists(folderPath))
            {
                return true;
            }
            try
            {
                Directory.Delete(folderPath);
                return true;
            }
            catch(Exception e)
            {
                Console.WriteLine($"Impossibile eliminare la directory {folderPath}:\n{e}");
                return false;
            }
        }
        

        private static async Task DownloadLS()
        {
            DateTime current = start;

            while (current != end)
            {
                string timeStr = current.ToString("HHmmss");

                string url = $"https://www.ls-x.de/_rpc/json/.lstc/instrument/list/lsxpretrades?time={timeStr}";
                int attempt = 0;
                while (true)
                {
                    try
                    {
                        using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                        Console.WriteLine($"Scaricato {url}");
                        using Stream stream = await response.Content.ReadAsStreamAsync();
                        using var reader = new StreamReader(stream);
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            try
                            {
                                ProcessLsLine(line);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Impossibile processare linea LS:\n" + e.ToString());
                            }
                        }
                        WriteOutput(LS, lsState);
                        current = current.AddMinutes(5);
                        break;
                    }
                    catch (Exception ex)
                    {
                        var waitSeconds = attempt * 5;

                        Console.WriteLine(
                            $"  Errore LS: {ex}\n" +
                            $"  Nuovo tentativo tra {waitSeconds} secondi..."
                        );

                        await Task.Delay(waitSeconds * 1000);
                        attempt++;
                    }
                }
            }
        }

       private static async Task DownloadEIX()
        {
            DateTime current = start;

            while (current != end)
            {
                long currentMs = new DateTimeOffset(current, TimeSpan.Zero).ToUnixTimeMilliseconds();
                string dateStr = current.ToString("yyyy-MM-dd");
                string timeStr = current.ToString("HH.mm");

                string url =
                    $"https://european-investor-exchange.com/api/trade-file-contents?key=pretrade/{dateStr}/Pretrade.{currentMs}.csv&attachmentFilename=Pretrade_{dateStr}_{timeStr}.csv";

                int attempt = 0;

                while (true)
                {
                    try
                    {
                        Console.WriteLine($"Tentativo EIX {attempt} download di {url}");
                        using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead);
                        response.EnsureSuccessStatusCode();
                        Console.WriteLine($"Scaricato {url}");
                        string csv = await response.Content.ReadAsStringAsync();
                        foreach (var line in csv.Split('\n'))
                        {
                            try
                            {
                                ProcessEixLine(line);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Impossibile processare linea EIX:\n" + e);
                            }
                        }

                        WriteOutput(EIX, eixState);

                        current = current.AddMinutes(5);
                        break;
                    }
                    catch (Exception ex)
                    {
                        var waitSeconds = 5;

                        Console.WriteLine(
                            $"Errore EIX: {ex}\n" +
                            $"Nuovo tentativo tra {waitSeconds} secondi..."
                        );

                        await Task.Delay(waitSeconds * 1000);
                        attempt++;
                    }
                }
            }
        }

        private static async Task DownloadXetra()
        {
            string yearStr = year.ToString();
            string monthStr = month.ToString().PadLeft(2, '0');
            string dayStr = day.ToString().PadLeft(2, '0');

            DateTime current = start;

            while (current != end)
            {
                //https://mfs.deutsche-boerse.com/api/download/DETR-pretrade-2026-06-05T20_50.json.gz
                string hourStr = current.Hour.ToString().PadLeft(2, '0');
                string minutesStr = current.Minute.ToString().PadLeft(2, '0');
                string url = $"https://mfs.deutsche-boerse.com/api/download/DETR-pretrade-{yearStr}-{monthStr}-{dayStr}T{hourStr}_{minutesStr}.json.gz";
                int attempt = 0;
                while (true)
                {
                    try
                    {
                        //Console.WriteLine($"Tentativo XETRA {attempt}");
                        using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                        Console.WriteLine($"Scaricato {url}");
                        using Stream stream = await response.Content.ReadAsStreamAsync();
                        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
                        using var reader = new StreamReader(gzip);
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            try
                            {
                                ProcessXetraLine(line);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Impossibile processare linea Xetra:\n" + e.ToString());
                            }
                        }
                        WriteOutput(XETRA, xetraState);
                        current = current.AddMinutes(1);
                        break;
                    }
                    catch (Exception ex)
                    {
                        var waitSeconds = attempt * 5;

                        Console.WriteLine(
                            $"  Errore XETRA: {ex}\n" +
                            $"  Nuovo tentativo tra {waitSeconds} secondi..."
                        );

                        await Task.Delay(waitSeconds * 1000);
                        attempt++;
                    }
                }
            }
        }

        private static HashSet<string> ParseEtfIsins()
        {
            HashSet<string> etfIsins = [];
            string isinsFilePath = GetPath("isins.txt");
            foreach (string line in File.ReadLines(isinsFilePath))
            {
                if (!string.IsNullOrEmpty(line))
                {
                    etfIsins.Add(line.Trim());
                }
            }
            return etfIsins;
        }

        // -----------------------------
        // Modello dati
        // -----------------------------
        private class Entry
        {
            public double? Bid;
            public double? Ask;
            public int? BidQty;
            public int? AskQty;

            public Entry Copy() => new()
            {
                Bid = Bid,
                Ask = Ask,
                BidQty = BidQty,
                AskQty = AskQty,
            };

            public bool IsSame(Entry other)
            {
                return Bid == other.Bid && Ask == other.Ask && BidQty == other.BidQty && AskQty == other.AskQty;
            }
        }

        // -----------------------------
        // Utilità
        // -----------------------------
        private static DateTime ParseTime(string ts)
        {
            // Gestisce ISO con eventuale 'Z'
            ts = ts.Replace("Z", "+00:00");
            return DateTime.Parse(ts, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        private static DateTime FloorSecond(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, DateTimeKind.Utc);
        }

        private static double? SafeFloat(string s)
        {
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return v;
            return null;
        }

        private static int? SafeInt(string s)
        {
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                return (int)Math.Round(v);
            return null;
        }

        private static ConcurrentDictionary<DateTime, Entry> GetInstrumentState(
            ConcurrentDictionary<string, ConcurrentDictionary<DateTime, Entry>> state,
            string instr)
        {
            return state.GetOrAdd(instr, _ => new ConcurrentDictionary<DateTime, Entry>());
        }

        private static Entry GetEntry(
            ConcurrentDictionary<string, ConcurrentDictionary<DateTime, Entry>> state,
            string instr,
            DateTime dt)
        {
            var instrState = GetInstrumentState(state, instr);
            return instrState.GetOrAdd(dt, _ => new Entry());
        }

        // -----------------------------
        // XETRA (JSON DELTA FEED)
        // -----------------------------
        private static void ProcessXetraLine(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("instrumentIdentificationCode", out var instrProp))
                {
                    Console.WriteLine("Linea XETRA senza strumento:\n" + line);
                    return;
                }

                var isin = instrProp.GetString();

                if (string.IsNullOrEmpty(isin))
                {
                    Console.WriteLine("Linea XETRA senza ISIN:\n" + line);
                    return;
                }

                if (!etfIsins.Contains(isin))
                {
                    return;
                }

                if (!root.TryGetProperty("updateDateAndTime", out JsonElement tsProp))
                {
                    if (!root.TryGetProperty("mdupdateDateAndTime", out tsProp))
                    {
                        Console.WriteLine("Linea XETRA senza TS:\n" + line);
                        return;
                    }
                }

                var ts = tsProp.GetString();
                if (string.IsNullOrEmpty(ts))
                {
                    Console.WriteLine("Linea XETRA senza TS:\n" + line);
                    return;
                }

                DateTime dt = FloorSecond(ParseTime(ts));
                ConcurrentDictionary<DateTime, Entry> instrState = GetInstrumentState(xetraState, isin);
                Entry entry = instrState.GetOrAdd(dt, _ => xetraLastEntry.GetValueOrDefault(isin)?.Copy() ?? new Entry());

                xetraLastEntry[isin] = entry;

                double? newBid = null;
                int? newBidQty = null;
                double? newAsk = null;
                int? newAskQty = null;

                if (root.TryGetProperty("bestBid", out JsonElement bestBidProp) && bestBidProp.ValueKind != JsonValueKind.Null)
                {
                    newBid = bestBidProp.GetDouble();
                }
                else if (root.TryGetProperty("mdBidMktDepthGroup1", out JsonElement group))
                {
                    if (group.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement groupElement in group.EnumerateArray())
                        {
                            if (groupElement.TryGetProperty("price", out JsonElement price))
                            {
                                double bid = price.GetDouble();
                                if (bid == 0.0)
                                {
                                    continue;
                                }
                                newBid = bid;
                            }
                            if (groupElement.TryGetProperty("quantity", out JsonElement quantity))
                            {
                                int bidQty = (int)Math.Round(quantity.GetDouble());
                                if (bidQty == 0.0)
                                {
                                    continue;
                                }
                                newBidQty = bidQty;
                            }
                            break;
                        }

                    }
                }

                if (root.TryGetProperty("bestAsk", out var bestAskProp) && bestAskProp.ValueKind != JsonValueKind.Null)
                {
                    newAsk = bestAskProp.GetDouble();
                }
                else if (root.TryGetProperty("mdAskMktDepthGroup1", out JsonElement group))
                {
                    if (group.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement groupElement in group.EnumerateArray())
                        {
                            if (groupElement.TryGetProperty("price", out JsonElement price))
                            {
                                double ask = price.GetDouble();
                                if (ask == 0.0)
                                {
                                    continue;
                                }
                                newAsk = ask;
                            }
                            if (groupElement.TryGetProperty("quantity", out JsonElement quantity))
                            {
                                int askQty = (int)Math.Round(quantity.GetDouble());
                                if (askQty == 0.0)
                                {
                                    continue;
                                }
                                newAskQty = askQty;
                            }
                            break;
                        }
                    }
                }

                if (root.TryGetProperty("bestBidQty", out var bestBidQtyProp) && bestBidQtyProp.ValueKind != JsonValueKind.Null)
                {
                    newBidQty = (int)Math.Round(bestBidQtyProp.GetDouble());
                }

                if (root.TryGetProperty("bestAskQty", out var bestAskQtyProp) && bestAskQtyProp.ValueKind != JsonValueKind.Null)
                {
                    newAskQty = (int)Math.Round(bestAskQtyProp.GetDouble());
                }

                if (newBid.HasValue)
                {
                    entry.Bid = newBid.Value;
                }
                if (newAsk.HasValue)
                {
                    entry.Ask = newAsk;
                }
                if (newBidQty.HasValue)
                {
                    entry.BidQty = newBidQty.Value;
                }
                if (newAskQty.HasValue)
                {
                    entry.AskQty = newAskQty.Value;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Linea XETRA invalida:{e}\n{line}");
            }
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly NumberStyles NumStyle = NumberStyles.Float;

        private static void ProcessLsLine(string line)
        {
            var parts = line.Trim().Replace("\"", "").Split(';');
            if (parts.Length < 6)
            {
                Console.WriteLine("Linea LS invalida:\n" + line);
                return;
            }

            var isin = parts[0];
            if (!etfIsins.Contains(isin))
            {
                return;
            }
            var ts = parts[5];


            var bid = SafeFloat(parts[1]);
            var ask = SafeFloat(parts[2]);
            var bidQty = SafeInt(parts[3]);
            var askQty = SafeInt(parts[4]);

            try
            {
                var dt = FloorSecond(ParseTime(ts));
                var entry = GetEntry(lsState, isin, dt);

                if (bid == null)
                    Console.WriteLine($"Bid {parts[4]} non parsabile in linea LS {line}");
                else
                    entry.Bid = bid;

                if (ask == null)
                    Console.WriteLine($"Ask {parts[5]} non parsabile in linea LS {line}");
                else
                    entry.Ask = ask;

                if (bidQty == null)
                    Console.WriteLine($"BidQty {parts[2]} non parsabile in linea LS {line}");
                else
                    entry.BidQty = bidQty;

                if (askQty == null)
                    Console.WriteLine($"AskQty {parts[3]} non parsabile in linea LS {line}");
                else
                    entry.AskQty = askQty;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Linea LS errore: {e.Message}\n{line}");
            }
        }

        // -----------------------------
        // EIX (comma snapshot)
        // -----------------------------
        private static void ProcessEixLine(string line)
        {
            var p = line.Trim().Split(',');
            if (p.Length < 7)
            {
                Console.WriteLine("Linea EIX invalida:\n" + line);
                return;
            }

            var isin = p[1];
            if (!etfIsins.Contains(isin))
            {
                return;
            }

            var ts = p[0];

            var bidQty = SafeInt(p[2]);
            var askQty = SafeInt(p[3]);
            var bid = SafeFloat(p[4]);
            var ask = SafeFloat(p[5]);

            try
            {
                var dt = FloorSecond(ParseTime(ts));
                var entry = GetEntry(eixState, isin, dt);

                if (bid == null)
                    Console.WriteLine($"Bid {p[4]} non parsabile in linea EIX {line}");
                else
                    entry.Bid = bid;

                if (ask == null)
                    Console.WriteLine($"Ask {p[5]} non parsabile in linea EIX {line}");
                else
                    entry.Ask = ask;

                if (bidQty == null)
                    Console.WriteLine($"BidQty {p[2]} non parsabile in linea EIX {line}");
                else
                    entry.BidQty = bidQty;

                if (askQty == null)
                    Console.WriteLine($"AskQty {p[3]} non parsabile in linea EIX {line}");
                else
                    entry.AskQty = askQty;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Linea EIX errore: {e.Message}\n{line}");
            }
        }

        private static void WriteOutput(
            string name,
            ConcurrentDictionary<string, ConcurrentDictionary<DateTime, Entry>> state)
        {
            Console.WriteLine($"Writing {name} output");
            var outDir = name;
            string path = GetPath(outDir);
            Directory.CreateDirectory(path);

            // Parallelizza per strumento, ma ogni file è indipendente
            Parallel.ForEach(state, kv =>
            {
                var instr = kv.Key;
                var times = kv.Value;

                var outFile = Path.Combine(path, $"{instr}.txt");

                // Scrittura sequenziale per file (ma i file sono diversi tra loro)
                using var writer = new StreamWriter(outFile, append: true);
                foreach (var pair in times.OrderBy(p => p.Key))
                {
                    var dt = pair.Key;
                    var entry = pair.Value;

                    if (
                        entry.Bid == null
                        || entry.Bid == 0
                        || entry.Ask == null
                        || entry.Ask == 0
                        || entry.BidQty == null
                        || entry.BidQty == 0
                        || entry.AskQty == null
                        || entry.AskQty == 0
                    )
                        continue;

                    writer.WriteLine($"{dt.ToString("o", CultureInfo.InvariantCulture)};{entry.Bid};{entry.Ask};{entry.BidQty};{entry.AskQty}");
                }
            });
            state.Clear();
        }

        private static string GetPath(string path) => $"data/{path}";
    }
}