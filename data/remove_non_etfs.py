from pathlib import Path
import shutil

# File con gli ISIN da mantenere
isins_file = Path("isins.txt")

# Cartelle da controllare
folders = [
    Path("eix_filtrato"),
    Path("ls_filtrato"),
    Path("xetra_filtrato"),
]

# Carica gli ISIN in un set
with isins_file.open("r", encoding="utf-8") as f:
    valid_isins = {
        line.strip()
        for line in f
        if line.strip()
    }

for folder in folders:
    if not folder.exists():
        print(f"Cartella non trovata: {folder}")
        continue

    trash_folder = folder / "da_cestinare"
    trash_folder.mkdir(exist_ok=True)

    moved = 0

    for file_path in folder.glob("*.txt"):
        if file_path.parent == trash_folder:
            continue

        isin = file_path.stem  # nome file senza .txt

        if isin not in valid_isins:
            destination = trash_folder / file_path.name
            shutil.move(str(file_path), str(destination))
            moved += 1

    print(f"{folder}: spostati {moved} file")

print("Operazione completata.")