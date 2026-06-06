import re

INPUT_FILE = "etfs.htm"
OUTPUT_FILE = "isins.txt"

with open(INPUT_FILE, "r", encoding="utf-8", errors="ignore") as f:
    html = f.read()

isins = sorted(set(re.findall(r'\b[A-Z]{2}[A-Z0-9]{10}\b', html)))

with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
    for isin in isins:
        f.write(f'{isin}\n')

print(f"Trovati {len(isins)} ISIN unici.")
print(f"Salvati in {OUTPUT_FILE}")