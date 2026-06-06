import os
import json
from datetime import datetime
from collections import defaultdict

ROOT = "."

XETRA = "xetra"
LS = "ls"
EIX = "eix"

xetra_state = defaultdict(lambda: defaultdict(lambda: {"bid": None, "ask": None, "bid_qty": None, "ask_qty": None}))
ls_state = defaultdict(lambda: defaultdict(lambda: {"bid": None, "ask": None, "bid_qty": None, "ask_qty": None}))
eix_state = defaultdict(lambda: defaultdict(lambda: {"bid": None, "ask": None, "bid_qty": None, "ask_qty": None}))

last_xetra_bid_asks = dict()

def parse_time(ts: str):
    return datetime.fromisoformat(ts.replace("Z", "+00:00"))

def floor_second(dt):
    return dt.replace(microsecond=0)

def safe_float(x):
    try:
        return float(x)
    except:
        return None

def safe_int(x):
    try:
        return int(round(float(x)))
    except:
        return None


# -----------------------------
# XETRA (JSON DELTA FEED)
# -----------------------------
def process_xetra_line(line):
    try:
        obj = json.loads(line)
    except:
        print("Linea XETRA invalida:\n" + line)
        return

    instr = obj.get("instrumentIdentificationCode")
    ts = obj.get("updateDateAndTime")
    if not instr or not ts:
        print("Linea XETRA senza strumento o TS:\n" + line)
        return

    dt = floor_second(parse_time(ts))
    entry = xetra_state[instr][dt]

    if "bestBid" in obj:
        entry["bid"] = obj["bestBid"]

    if "bestAsk" in obj:
        entry["ask"] = obj["bestAsk"]

    if "bestBidQty" in obj:
        entry["bid_qty"] = obj["bestBidQty"]

    if "bestAskQty" in obj:
        entry["ask_qty"] = obj["bestAskQty"]


# -----------------------------
# LS (semicolon snapshot)
# -----------------------------
def process_ls_line(line):
    p = line.split(";")
    if len(p) < 6:
        print("Linea LS invalida:\n" + line)
        return

    instr = p[0]
    bid = safe_float(p[1])
    ask = safe_float(p[2])
    bid_qty = safe_int(p[3])
    ask_qty = safe_int(p[4])
    ts = p[5]

    if not ts:
        print("Linea LS senza TS:\n" + line)
        return

    dt = floor_second(parse_time(ts.replace(" ", "T")))
    entry = ls_state[instr][dt]

    if bid is None:
        print(f"Bid {p[1]} non parsabile in linea LS {line}")
    else:
        entry["bid"] = bid
    if ask is None:
        print(f"Ask {p[2]} non parsabile in linea LS {line}")
    else:
        entry["ask"] = ask

    if bid_qty is None:
        print(f"Bid {p[1]} non parsabile in linea LS {line}")
    else:
        entry["bid_qty"] = bid_qty

    if ask_qty is None:
        print(f"Ask {p[2]} non parsabile in linea LS {line}")
    else:
        entry["ask_qty"] = ask_qty



# -----------------------------
# EIX (comma snapshot)
# -----------------------------
def process_eix_line(line):
    p = line.strip().split(",")
    if len(p) < 7:
        print("Linea EIX invalida:\n" + line)
        return

    ts = p[0]
    instr = p[1]
    bid = safe_float(p[4])
    ask = safe_float(p[5])
    bid_qty = safe_int(p[2])
    ask_qty = safe_int(p[3])

    try:
        dt = floor_second(parse_time(ts))
        entry = eix_state[instr][dt]
        if bid is None:
            print(f"Bid {p[1]} non parsabile in linea EIX {line}")
        else:
            entry["bid"] = bid

        if ask is None:
            print(f"Ask {p[2]} non parsabile in linea EIX {line}")
        else:
            entry["ask"] = ask

        if bid_qty is None:
            print(f"Bid {p[1]} non parsabile in linea EIX {line}")
        else:
            entry["bid_qty"] = bid_qty

        if ask_qty is None:
            print(f"Ask {p[2]} non parsabile in linea EIX {line}")
        else:
            entry["ask_qty"] = ask_qty

    except Exception as e:
        print(f"Linea EIX errore: {e}\n{line}")
        return


# -----------------------------
# PROCESS FILES
# -----------------------------
def process_folder(folder):
    path = os.path.join(ROOT, folder)

    if not os.path.isdir(path):
        return []

    results = []

    for file in os.listdir(path):
        full = os.path.join(path, file)
        print(f"Processing file {file}")

        with open(full, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue

                if folder == XETRA:
                    process_xetra_line(line)

                elif folder == LS:
                    r = process_ls_line(line)
                    if r:
                        results.append(r)

                elif folder == EIX:
                    r = process_eix_line(line)
                    if r:
                        results.append(r)
        state = None
        if folder == XETRA:
            state = write_output(folder, xetra_state)
        elif folder == EIX:
            state = write_output(folder, eix_state)
        else:
            state = write_output(folder, ls_state)


        if folder == XETRA:
            xetra_state.clear()
        elif folder == EIX:
            eix_state.clear()
        else:
            ls_state.clear()

    return results

def write_output(name, state):
    print(f"Writing {name} output")
    out_dir = name + "_filtrato"
    os.makedirs(out_dir, exist_ok=True)

    for instr, times in state.items():
        out_file = os.path.join(out_dir, f"{instr}.txt")
        with open(out_file, "a", encoding="utf-8") as f:
            for dt, entry in times.items():

                bid = entry["bid"]
                ask = entry["ask"]
                bid_qty = entry["bid_qty"]
                ask_qty = entry["ask_qty"]

                if bid is None or ask is None or bid_qty is None or ask_qty is None:
                    continue
                f.write(f"{instr};{dt.isoformat()};{bid};{ask};{bid_qty};{ask_qty}\n")

print("Starting EIX data")
process_folder(EIX)
print(f"Completed EIX data - obtained {len(eix_state)} data points")

print("Starting XETRA data")
process_folder(XETRA)
print(f"Completed XETRA data - obtained {len(xetra_state)} data points")

print("Starting LS data")
process_folder(LS)
print(f"Completed LS data - obtained {len(ls_state)} data points")

print("DONE")