using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BidAskMarkets
{
    public partial class Program
    {
        private static readonly HashSet<string> biggestEtfsIsins = ["IE00B5BMR087", "IE00B4L5Y983", "IE00B3XXRP09", "IE00BK5BQT80", "IE00BKM4GZ66", "IE00B3YCGJ38", "IE00B4ND3602", "IE00B53SZB19", "IE00B3RBWM25", "DE000A0S9GB0", "LU0290358497", "LU0908500753"];

        public static async Task Main(string[] args)
        {
            Console.WriteLine("Inserisci un isin specifico di un ETF, o lascia vuoto per fare l'analisi completa:");
            string? isin = Console.ReadLine();

            try
            {
                if (string.IsNullOrEmpty(isin))
                {
                    await Filter.Run();
                    //await Run();
                }
                else
                {
                    RunForSingleETF(isin);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Errore: " + e.ToString());
            }

            Console.WriteLine("Premi qualsiasi pulsante per uscire.");
            Console.ReadLine();
        }

        private static async Task Run()
        {
            var lsDictionaryTask = GetSpreadsAsync("ls");
            var xetraDictionaryTask = GetSpreadsAsync("xetra");
            var eixDictionaryTask = GetSpreadsAsync("eix");

            Dictionary<string, SpreadData> lsDictionary = await lsDictionaryTask;
            Dictionary<string, SpreadData> xetraDictionary = await xetraDictionaryTask;
            Dictionary<string, SpreadData> eixDictionary = await eixDictionaryTask;

            double xetraEixAverageSum = 0;
            double xetraLsAverageSum = 0;
            int xetraEixAverageCount = 0;
            int xetraLsAverageCount = 0;
            foreach (string key in xetraDictionary.Keys)
            {
                SpreadData xetra = xetraDictionary[key];
                if (eixDictionary.TryGetValue(key, out SpreadData? eix))
                {
                    double xetraEixAverage = eix.Average() - xetra.Average();
                    xetraEixAverageSum += xetraEixAverage;
                    xetraEixAverageCount++;

                    if (lsDictionary.TryGetValue(key, out SpreadData? ls))
                    {
                        double xetraLsAverage = ls.Average() - xetra.Average();
                        xetraLsAverageSum += xetraLsAverage;
                        xetraLsAverageCount++;
                        if (biggestEtfsIsins.Contains(key))
                        {
                            Console.WriteLine($"{key}:");
                            PrintResult(xetra, "XETRA");
                            PrintResult(eix, "EIX");
                            PrintResult(ls, "LS");
                            Console.WriteLine($"Differenza Xetra-EIX media: {xetraEixAverage.Format()}%");
                            Console.WriteLine($"Differenza Xetra-LS media: {xetraLsAverage.Format()}%");
                            Console.WriteLine();
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("-------------------");
            Console.WriteLine();
            Console.WriteLine("Risultati aggregati:");
            double xetraEixAverageDifference = xetraEixAverageSum / xetraEixAverageCount;
            double xetraLsAverageDifference = xetraLsAverageSum / xetraLsAverageCount;
            Console.WriteLine($"Differenza media tra XETRA ed EIX: {xetraEixAverageDifference.Format()}%");
            Console.WriteLine($"Differenza media tra XETRA ed LS: {xetraLsAverageDifference.Format()}%");
        }

        private static void RunForSingleETF(string isin)
        {
            Dictionary<string, SpreadData> lsDictionary = [];
            Dictionary<string, SpreadData> eixDictionary = [];
            Dictionary<string, SpreadData> xetraDictionary = [];

            GetSpreads(lsDictionary, $"C:\\repos\\BidAskMarkets\\data\\ls_filtrato\\{isin}.txt", isin);
            GetSpreads(eixDictionary, $"C:\\repos\\BidAskMarkets\\data\\eix_filtrato\\{isin}.txt", isin);
            GetSpreads(xetraDictionary, $"C:\\repos\\BidAskMarkets\\data\\xetra_filtrato\\{isin}.txt", isin);

            SpreadData xetra = xetraDictionary[isin];
            if (eixDictionary.TryGetValue(isin, out SpreadData? eix))
            {
                double xetraEixAverage = eix.Average() - xetra.Average();

                if (lsDictionary.TryGetValue(isin, out SpreadData? ls))
                {
                    double xetraLsAverage = ls.Average() - xetra.Average();
                    Console.WriteLine();
                    PrintResult(xetra, "XETRA");
                    PrintResult(eix, "EIX");
                    PrintResult(ls, "LS");
                    Console.WriteLine($"Differenza Xetra-EIX media: {xetraEixAverage.Format()}%");
                    Console.WriteLine($"Differenza Xetra-LS media: {xetraLsAverage.Format()}%");
                    Console.WriteLine();
                }
            }
        }


        private static void PrintResult(SpreadData result, string name)
        {
            Console.WriteLine($"Spread {name}: Medio: {result.Average().Format()}% - Minimo: {result.Min.Format()}% - Massimo: {result.Max.Format()}%");
        }

        private static readonly CultureInfo cultureInfo = new("it-IT");

        private static Task<Dictionary<string, SpreadData>> GetSpreadsAsync(string folderName)
        {
            return Task.Run(delegate
            {
                Dictionary<string, SpreadData> spreadPercentages = [];
                foreach (string file in Directory.EnumerateFiles($"C:\\repos\\BidAskMarkets\\data\\{folderName}\\"))
                {
                    string isin = Path.GetFileNameWithoutExtension(file);
                    GetSpreads(spreadPercentages, file, isin);
                }

                return spreadPercentages;
            });
        }

        private static void GetSpreads(Dictionary<string, SpreadData> spreadPercentages, string file, string isin)
        {
            foreach (string line in File.ReadLines(file))
            {
                string[] elements = line.Split(';');

                if (!spreadPercentages.TryGetValue(isin, out SpreadData? spreadData))
                {
                    spreadData = new SpreadData(isin);
                    spreadPercentages[isin] = spreadData;
                }
                if (double.TryParse(elements[1], cultureInfo, out double bid))
                {
                    if (double.TryParse(elements[2], cultureInfo, out double ask))
                    {
                        if (ask == 0.0 || bid == 0.0)
                        {
                            continue;
                        }
                        double spread = ask - bid;
                        if (spread <= 0)
                        {
                            continue;
                        }
                        double spreadPercentage = spread / ((ask + bid) / 2);
                        spreadData.Update(spreadPercentage);
                    }
                }
            }
        }

        [GeneratedRegex("\"bestAsk\":([^,\"]+)")]
        private static partial Regex BestAskRegex();

        [GeneratedRegex("\"bestBid\":([^,\"]+)")]
        private static partial Regex BestBidRegex();

        [GeneratedRegex("\"instrumentIdentificationCode\":\"([^\"]+)\"")]
        private static partial Regex IsinRegex();

        private static Dictionary<string, SpreadData> Xetra()
        {
            string xetraDirectory = "C:\\repos\\BidAskMarkets\\data\\xetra";
            Regex isinRegex = IsinRegex();
            Regex bidRegex = BestBidRegex();
            Regex askRegex = BestAskRegex();

            ConcurrentDictionary<string, SpreadData> spreadDataDict = [];
            List<Task> tasks = [];

            foreach (string file in Directory.EnumerateFiles(xetraDirectory))
            {
                Task task = Task.Run(delegate
                {
                    foreach (string line in File.ReadLines(file))
                    {
                        string? isin = isinRegex.Matches(line).FirstOrDefault()?.Groups[1].Value;

                        if (isin == null)
                        {
                            continue;
                        }

                        if (!isin.StartsWith("IE") && !isin.StartsWith("LU"))
                        {
                            continue;
                        }

                        if (!spreadDataDict.TryGetValue(isin, out SpreadData? spreadData))
                        {
                            spreadData = new SpreadData(isin);
                            spreadDataDict[isin] = spreadData;
                        }

                        string? bidString = bidRegex.Matches(line).FirstOrDefault()?.Groups[1].Value;
                        double? bid = null;
                        if (bidString != null)
                        {
                            bid = double.Parse(bidString, CultureInfo.InvariantCulture);
                        }

                        string? askString = askRegex.Matches(line).FirstOrDefault()?.Groups[1].Value;
                        double? ask = null;
                        if (askString != null)
                        {
                            ask = double.Parse(askString, CultureInfo.InvariantCulture);
                        }

                        if (bid.HasValue)
                        {
                            if (ask.HasValue)
                            {
                                double spread = ask.Value - bid.Value;
                                if (spread == 0)
                                {
                                    continue;
                                }
                                double spreadPercentage = spread / ((ask.Value + bid.Value) / 2);
                                spreadData.Update(spreadPercentage);
                            }
                        }
                    }
                });

                tasks.Add(task);

            }

            Task.WaitAll(tasks);

            return spreadDataDict.ToDictionary();
        }

        private static Dictionary<string, SpreadData> EIX()
        {
            ConcurrentDictionary<string, SpreadData> spreadPercentages = [];
            List<Task> tasks = [];

            foreach (string file in Directory.EnumerateFiles("C:\\repos\\BidAskMarkets\\data\\eix\\"))
            {
                Task task = Task.Run(delegate
                {
                    foreach (string line in File.ReadLines(file))
                    {
                        string[] elements = line.Split(',');
                        string isin = elements[1];
                        if (!spreadPercentages.TryGetValue(isin, out SpreadData? spreadData))
                        {
                            spreadData = new SpreadData(isin);
                            spreadPercentages[isin] = spreadData;
                        }
                        if (double.TryParse(elements[4], CultureInfo.InvariantCulture, out double bid))
                        {
                            if (double.TryParse(elements[5], CultureInfo.InvariantCulture, out double ask))
                            {
                                double spread = ask - bid;
                                if (spread == 0)
                                {
                                    continue;
                                }
                                double spreadPercentage = spread / ((ask + bid) / 2);
                                spreadData.Update(spreadPercentage);
                            }
                        }
                    }
                });
                tasks.Add(task);
            }

            Task.WaitAll(tasks);

            return spreadPercentages.ToDictionary();
        }


        private static Dictionary<string, SpreadData> LS()
        {
            ConcurrentDictionary<string, SpreadData> spreadPercentages = [];
            List<Task> tasks = [];

            foreach (string file in Directory.EnumerateFiles("C:\\repos\\BidAskMarkets\\data\\ls\\"))
            {
                Task task = Task.Run(delegate
                {
                    foreach (string line in File.ReadLines(file))
                    {
                        string[] elements = line.Split(';');
                        string isin = elements[0];

                        if (!spreadPercentages.TryGetValue(isin, out SpreadData? spreadData))
                        {
                            spreadData = new SpreadData(isin);
                            spreadPercentages[isin] = spreadData;
                        }
                        if (double.TryParse(elements[1], CultureInfo.InvariantCulture, out double bid))
                        {
                            if (double.TryParse(elements[2], CultureInfo.InvariantCulture, out double ask))
                            {
                                double spread = ask - bid;
                                if (spread == 0)
                                {
                                    continue;
                                }
                                double spreadPercentage = spread / ((ask + bid) / 2);
                                spreadData.Update(spreadPercentage);
                            }
                        }
                    }
                });
                tasks.Add(task);
            }

            Task.WaitAll(tasks);

            return spreadPercentages.ToDictionary();
        }


    }

    public static class DoubleExtensions
    {
        private const int decimals = 4;

        public static string Format(this double value)
        {
            return (value * 100.0).ToString($"0.{new string('#', decimals)}", CultureInfo.InvariantCulture);
        }
    }

    public class SpreadData(string isin)
    {
        public string Isin { get; } = isin;

        public double Sum = 0.0;
        public int Count = 0;
        public double Min = double.MaxValue;
        public double Max = double.MinValue;

        public void Update(double newSpread)
        {
            Sum += newSpread;
            Count++;
            Min = Math.Min(Min, newSpread);
            Max = Math.Max(Max, newSpread);
        }

        public double Average()
        {
            if (Count == 0)
            {
                return 0.0;
            }
            return Sum / Count;
        }
    }
}
