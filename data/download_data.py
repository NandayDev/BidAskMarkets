import gzip
import os
import time
import requests
import time
import datetime
from pathlib import Path
import json

OUTPUT_DIR_XETRA = r"C:\repos\BidAskMarkets\data\xetra"

os.makedirs(OUTPUT_DIR_XETRA, exist_ok=True)

BASE_URL = (
    "https://mfs.deutsche-boerse.com/api/download/"
    "DETR-pretrade-{timestamp}.json.gz"
)

with open("isins.txt", "r", encoding = "utf-8") as isins_file:
    etf_isins = { line.strip() for line in isins_file }

# --- INPUT DATA ---
anno_from_user = input("Anno (YYYY): ")
anno = int(anno_from_user)
mese_from_user = input("Mese (MM): ")
mese = int(mese_from_user)
giorno_from_user = input("Giorno (DD): ")
giorno = int(giorno_from_user)

def generate_timestamps():
    start = datetime.datetime.strptime("07:05", "%H:%M")
    end = datetime.datetime.strptime("15:24", "%H:%M")

    current = start
    while current <= end:
        yield current.strftime("%H_%M")
        current += datetime.timedelta(minutes=1)


def download_with_retry(url, destination):
    attempt = 1

    while True:
        try:
            print(f"  Tentativo {attempt}")

            response = requests.get(url, timeout=30)

            if response.status_code == 404:
                # file inesistente: inutile ritentare
                return False

            response.raise_for_status()

            with open(destination, "wb") as f:
                f.write(response.content)

            return True

        except Exception as e:
            wait_seconds = attempt * 5

            print(
                f"  Errore: {e}\n"
                f"  Nuovo tentativo tra {wait_seconds} secondi..."
            )

            time.sleep(wait_seconds)
            attempt += 1

def download_xetra():
    for hhmm in generate_timestamps():
        timestamp = f"{anno_from_user}-{mese_from_user.zfill(2)}-{giorno_from_user.zfill(2)}T{hhmm}"
        url = BASE_URL.format(timestamp=timestamp)

        gz_path = os.path.join(
            OUTPUT_DIR_XETRA,
            f"DETR-pretrade-{timestamp}.json.gz"
        )

        json_path = gz_path[:-3]

        print(f"\nScarico {url}")

        success = download_with_retry(url, gz_path)

        if not success:
            print("  -> file non disponibile")
            continue

        try:
            with gzip.open(gz_path, "rt", encoding="utf-8") as src, \
                open(json_path, "w", encoding="utf-8") as dst:

                kept = 0

                for line in src:
                    try:
                        obj = json.loads(line)
                    except:
                        print("Linea XETRA invalida:\n" + line)
                        continue
                    if "bestBid" in obj or "bestAsk" in obj:
                        if "instrumentIdentificationCode" in obj:
                            if obj["instrumentIdentificationCode"] in etf_isins: 
                                dst.write(line)
                                kept += 1

            os.remove(gz_path)

            print(f"  -> OK ({kept} righe mantenute)")

        except Exception as e:
            print(f"  -> errore durante decompressione/filtro: {e}")


def ms_since_epoch_utc(dt):
    return int(dt.timestamp() * 1000)


def download_iex():
    # intervallo 09:00 → 17:30
    start = datetime.datetime(anno, mese, giorno, 7, 5, tzinfo=datetime.timezone.utc)
    end   = datetime.datetime(anno, mese, giorno, 15, 25, tzinfo=datetime.timezone.utc)

    current = start

    while current <= end:
        # timestamp in ms
        ts_ms = ms_since_epoch_utc(current)

        # formattazioni
        date_str = current.strftime("%Y-%m-%d")
        time_str = current.strftime("%H.%M")

        # URL dinamico
        url = (
            "https://european-investor-exchange.com/api/trade-file-contents"
            f"?key=pretrade/{date_str}/Pretrade.{ts_ms}.csv"
            f"&attachmentFilename=Pretrade_{date_str}_{time_str}.csv"
        )

        filename = f"C:\\repos\\BidAskMarkets\\data\\eix\\Pretrade_{date_str}_{time_str}.csv"

        print(f"Scarico {filename} ...")

        # --- RETRY con backoff incrementale ---
        attempt = 1
        wait = 5

        while True:
            try:
                r = requests.get(url, timeout=20)
                if r.status_code == 200:
                    with open(filename, "wb") as f:
                        for raw_line in r.iter_lines():
                            line = raw_line.decode("utf-8")
                            parts = line.split(",")
                            isin = parts[1]
                            if isin in etf_isins:
                                f.write(f"{line}\n")
                    print(f"Salvato {filename}")
                    break
                else:
                    print(f"Errore HTTP {r.status_code}, retry tra {wait}s...")
            except Exception as e:
                print(f"Errore: {e}, retry tra {wait}s...")

            time.sleep(wait)
            wait += 5
            attempt += 1

        # passo al minuto successivo
        current += datetime.timedelta(minutes=5)

    print("Download IEX completato.")

def download_ls():
    start = datetime.datetime(anno, mese, giorno, 7, 5, tzinfo=datetime.timezone.utc)
    end   = datetime.datetime(anno, mese, giorno, 15, 25, tzinfo=datetime.timezone.utc)

    current = start

    while current <= end:
        time_str = current.strftime("%H%M%S")

        filename = f"C:\\repos\\BidAskMarkets\\data\\ls\\{time_str}.csv"

        if Path(filename).exists():
            print(f"File {filename} esiste già")
            current += datetime.timedelta(minutes = 5)
            continue

        print(f"Scarico {filename} ...")

        # URL dinamico
        url = (
            f"https://www.ls-x.de/_rpc/json/.lstc/instrument/list/lstcpretradesyesterday?time={time_str}"
        )


        # --- RETRY con backoff incrementale ---
        attempt = 1
        wait = 5

        while True:
            try:
                r = requests.get(url, timeout=20)

                if r.status_code == 200:
                    with open(filename, "wb") as f:
                        for raw_line in r.iter_lines():
                            line = raw_line.decode("utf-8")
                            parts = line.split(";")
                            isin = parts[0]
                            if isin in etf_isins:
                                f.write(f"{line}\n")
                    print(f"Salvato {filename}")
                    break
                else:
                    print(f"Errore HTTP {r.status_code}, retry tra {wait}s...")
            except Exception as e:
                print(f"Errore: {e}, retry tra {wait}s...")

            time.sleep(wait)
            wait += 5
            attempt += 1

        # passo al minuto successivo
        current += datetime.timedelta(minutes=5)

    print("Download completato.")

download_xetra()
download_iex()
download_ls()