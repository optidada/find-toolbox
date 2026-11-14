"""
Run with both test sites started (see README). Writes evaluation.json.

Search quality evaluation on the product demo: plain Find (OR / AND, built-in synonyms) vs the Find Toolbox.
Relevant products per query are judged from the product data.
"""
import json, sys, urllib.parse, urllib.request


# name, category, brand
P = [
 ("Contoso UltraBook 14 Laptop","Laptops","Contoso"),("Fabrikam Pro Notebook 16","Laptops","Fabrikam"),("Northwind Air Laptop 13","Laptops","Northwind"),
 ("Litware Gaming Laptop 17","Laptops","Litware"),("Tailspin Student Notebook","Laptops","Tailspin"),("Contoso Business Laptop 15","Laptops","Contoso"),
 ("Fabrikam Convertible Notebook","Laptops","Fabrikam"),("Northwind Creator Notebook","Laptops","Northwind"),
 ("Contoso Smartphone X","Phones","Contoso"),("Fabrikam Mobile Phone Lite","Phones","Fabrikam"),("Northwind Smartphone Pro","Phones","Northwind"),
 ("Litware Rugged Mobile Phone","Phones","Litware"),("Tailspin Smartphone Mini","Phones","Tailspin"),("Contoso Smartphone Max","Phones","Contoso"),
 ("Fabrikam Flip Phone","Phones","Fabrikam"),("Northwind Mobile Phone Classic","Phones","Northwind"),
 ("Contoso Wireless Headphones","Headphones","Contoso"),("Fabrikam Noise Cancelling Headphones","Headphones","Fabrikam"),("Northwind Sport Earbuds","Headphones","Northwind"),
 ("Litware Studio Headphones","Headphones","Litware"),("Tailspin Kids Headphones","Headphones","Tailspin"),("Contoso True Wireless Earbuds","Headphones","Contoso"),
 ("Fabrikam Over Ear Headphones","Headphones","Fabrikam"),("Northwind Gaming Headset","Headphones","Northwind"),
 ("Contoso 55 inch 4K TV","TVs","Contoso"),("Fabrikam OLED Television 65","TVs","Fabrikam"),("Northwind Smart TV 43","TVs","Northwind"),
 ("Litware QLED Television 75","TVs","Litware"),("Tailspin Kitchen TV 24","TVs","Tailspin"),("Contoso 8K Television 85","TVs","Contoso"),
 ("Fabrikam Outdoor TV 50","TVs","Fabrikam"),("Northwind Portable TV 32","TVs","Northwind"),
 ("Contoso Mirrorless Camera","Cameras","Contoso"),("Fabrikam Action Camera 4K","Cameras","Fabrikam"),("Northwind Instant Camera","Cameras","Northwind"),
 ("Litware DSLR Camera","Cameras","Litware"),("Tailspin Kids Camera","Cameras","Tailspin"),("Contoso Compact Camera","Cameras","Contoso"),
 ("Fabrikam Vlogging Camera","Cameras","Fabrikam"),("Northwind 360 Camera","Cameras","Northwind"),
]
cat = lambda c: {n for n, cc, b in P if cc == c}
brandcat = lambda br, c: {n for n, cc, b in P if cc == c and b == br}
one = lambda n: {n}

# (query, relevant set, what kind of query)
QUERIES = [
    ("laptop", cat("Laptops"), "synonym"),
    ("notebook", cat("Laptops"), "synonym"),
    ("tv", cat("TVs"), "synonym"),
    ("television", cat("TVs"), "synonym"),
    ("smartphone", cat("Phones") - one("Fabrikam Flip Phone"), "multi-word synonym"),
    ("mobile phone", cat("Phones") - one("Fabrikam Flip Phone"), "multi-word synonym"),
    ("earbuds", cat("Headphones") - one("Northwind Gaming Headset"), "one-way synonym"),
    ("fabrikam laptop", brandcat("Fabrikam", "Laptops"), "brand + synonym"),
    ("northwind tv", brandcat("Northwind", "TVs"), "brand + synonym"),
    ("fabrikam camera", brandcat("Fabrikam", "Cameras"), "brand + category"),
    ("gaming laptop", one("Litware Gaming Laptop 17"), "specific"),
    ("noise cancelling headphones", one("Fabrikam Noise Cancelling Headphones"), "specific"),
    ("kids camera", one("Tailspin Kids Camera"), "specific"),
    ("air laptop 13", one("Northwind Air Laptop 13"), "specific"),
    ("ait laptop 13", one("Northwind Air Laptop 13"), "typo"),
    ("fabrikam camra", brandcat("Fabrikam", "Cameras"), "typo"),
    ("headphnes", cat("Headphones") - one("Northwind Gaming Headset"), "typo"),
    ("televisoin", cat("TVs"), "typo + synonym"),
    ("cam", cat("Cameras"), "partial word"),
]

ENGINES = [("find", "Find, OR"), ("find-and", "Find, AND"), ("toolbox", "Find Toolbox")]

import time

def search(base, q, engine):
    url = base + "/toolbox-test/products?" + urllib.parse.urlencode({"q": q, "engine": engine})
    for attempt in range(6):
        try:
            time.sleep(0.4)  # the Find service rate limits (HTTP 429)
            d = json.load(urllib.request.urlopen(url, timeout=60))
            return [h["name"] for h in d["hits"]], d["total"]
        except urllib.error.HTTPError:
            time.sleep(2 * (attempt + 1))
    raise RuntimeError("failed: " + url)

def evaluate(base):
    rows, totals = [], {e: {"recall": 0, "precision": 0, "rr": 0, "zero": 0, "perfect": 0} for e, _ in ENGINES}
    for q, relevant, kind in QUERIES:
        row = [q, kind, len(relevant)]
        for engine, _ in ENGINES:
            hits, total = search(base, q, engine)
            found = [h for h in hits if h in relevant]
            recall = len(found) / len(relevant)
            precision = len(found) / len(hits) if hits else 0
            first = next((i + 1 for i, h in enumerate(hits) if h in relevant), None)
            rr = 1 / first if first else 0
            t = totals[engine]
            t["recall"] += recall; t["precision"] += precision; t["rr"] += rr
            t["zero"] += 1 if not hits else 0
            t["perfect"] += 1 if recall == 1 and precision == 1 else 0
            row.append((len(found), total, first))
        rows.append(row)
    n = len(QUERIES)
    summary = {e: {k: (v / n if k in ("recall", "precision", "rr") else v) for k, v in t.items()} for e, t in totals.items()}
    return rows, summary

if __name__ == "__main__":
    results = {}
    for site, base in [("cms12", "http://localhost:5112"), ("cms11", "http://localhost:5111")]:
        rows, summary = evaluate(base)
        results[site] = {"rows": rows, "summary": summary}
        print("==", site)
        for r in rows:
            cells = "  ".join("%-12s" % ("%d/%d of %d%s" % (f, r[2], t, "" if fr in (None, 1) else " #%d" % fr) if True else "") for f, t, fr in r[3:])
            print("%-28s %-20s %s" % (r[0], r[1], "  ".join("%s:%s" % (ENGINES[i][0], "%d/%d rel, %d hits, first %s" % (c[0], r[2], c[1], c[2])) for i, c in enumerate(r[3:]))))
        for e, label in ENGINES:
            s = summary[e]
            print("  %-13s recall %.0f%%  precision %.0f%%  MRR %.2f  zero-hit queries %d  perfect %d/%d" % (label, s["recall"] * 100, s["precision"] * 100, s["rr"], s["zero"], s["perfect"], len(QUERIES)))
    json.dump(results, open("evaluation.json", "w"), indent=1)
