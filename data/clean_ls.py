from pathlib import Path

folder = Path(r"C:\repos\BidAskMarkets\data\ls")  # cambia qui

for file in folder.glob("*.csv"):
    text = file.read_text(encoding="utf-8")
    text = text.replace('"', "")
    file.write_text(text, encoding="utf-8")

print("Fatto.")