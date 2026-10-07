"""Generate reproducible Conflux benchmark tables and charts from JSON result files.

No synthetic values are generated. Missing measurements remain explicitly missing.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import matplotlib.pyplot as plt
import pandas as pd


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", nargs="+", type=Path)
    parser.add_argument("--output", type=Path, default=Path("artifacts/benchmark-report"))
    args = parser.parse_args()

    records = []
    for path in args.input:
        with path.open("r", encoding="utf-8") as handle:
            value = json.load(handle)
        value["source"] = str(path)
        records.append(value)

    if not records:
        raise SystemExit("No benchmark result files were supplied.")

    args.output.mkdir(parents=True, exist_ok=True)
    frame = pd.DataFrame(records)
    frame.to_csv(args.output / "results.csv", index=False)
    (args.output / "results.json").write_text(json.dumps(records, indent=2), encoding="utf-8")

    numeric = [
        column for column in [
            "ThroughputRequestsPerSecond",
            "P50Milliseconds",
            "P95Milliseconds",
            "P99Milliseconds",
        ]
        if column in frame.columns
    ]
    for column in numeric:
        figure = plt.figure(figsize=(10, 5))
        axis = figure.add_subplot(1, 1, 1)
        axis.bar(frame.index.astype(str), frame[column])
        axis.set_title(column)
        axis.set_xlabel("Run")
        axis.set_ylabel(column)
        figure.tight_layout()
        figure.savefig(args.output / f"{column}.png", dpi=160)
        plt.close(figure)

    lines = [
        "# Conflux Benchmark Report",
        "",
        "Generated exclusively from supplied measured result files.",
        "No missing or unmeasured values are fabricated.",
        "",
        frame.to_markdown(index=False),
        "",
    ]
    (args.output / "REPORT.md").write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
