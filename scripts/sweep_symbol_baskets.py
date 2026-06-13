#!/usr/bin/env python3

from __future__ import annotations

import argparse
import csv
import datetime as dt
import itertools
import json
import math
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


@dataclass
class SweepResult:
    symbols: tuple[str, ...]
    size: int
    backtest_name: str
    output_dir: Path
    summary_path: Path | None
    sharpe: float
    net_profit: float
    drawdown: float
    fees: float
    win_rate: float
    loss_rate: float
    orders: int
    status: str
    error: str = ""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run LEAN backtests for every symbol combination of the requested sizes."
    )
    parser.add_argument(
        "--strategy",
        default="Strategies/PerpFundingStrat",
        help="Strategy directory to backtest.",
    )
    parser.add_argument(
        "--symbols",
        nargs="+",
        default=["BTC", "ETH", "BNB", "SOL", "ADA", "XRP"],
        help="Symbol universe. Accepts space-separated or comma-separated values.",
    )
    parser.add_argument(
        "--sizes",
        nargs="+",
        type=int,
        default=[2, 3],
        help="Combination sizes to test, for example 2 3.",
    )
    parser.add_argument(
        "--lean",
        default="lean",
        help="Lean CLI executable name or full path.",
    )
    parser.add_argument(
        "--output-root",
        default=None,
        help="Root folder for sweep outputs. Defaults to Strategies/PerpFundingStrat/backtests/sweeps/<timestamp>.",
    )
    parser.add_argument(
        "--no-update",
        action="store_true",
        help="Pass --no-update to lean backtest.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the planned runs without executing Lean.",
    )
    parser.add_argument(
        "--top",
        type=int,
        default=20,
        help="How many rows to show in the final ranking table.",
    )
    return parser.parse_args()


def flatten_symbols(raw_values: list[str]) -> list[str]:
    symbols: list[str] = []
    for value in raw_values:
        for token in value.split(","):
            symbol = token.strip().upper()
            if symbol and symbol not in symbols:
                symbols.append(symbol)
    return symbols


def parse_number(value: object) -> float:
    if value is None:
        return 0.0
    if isinstance(value, (int, float)):
        return float(value)
    text = str(value).strip()
    if not text:
        return 0.0
    text = text.replace("$", "").replace(",", "").replace("%", "")
    try:
        return float(text)
    except ValueError:
        return 0.0


def combo_label(symbols: Iterable[str]) -> str:
    return "__".join(symbols)


def find_summary_file(output_dir: Path) -> Path | None:
    candidates = sorted(output_dir.rglob("*summary.json"))
    if not candidates:
        candidates = sorted(output_dir.rglob("*-summary.json"))
    return candidates[-1] if candidates else None


def load_summary(summary_path: Path) -> dict:
    with summary_path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def extract_stats(summary: dict) -> dict:
    stats = summary.get("statistics", {})
    return {
        "sharpe": parse_number(stats.get("Sharpe Ratio")),
        "net_profit": parse_number(stats.get("Net Profit")),
        "drawdown": parse_number(stats.get("Drawdown")),
        "fees": parse_number(stats.get("Total Fees")),
        "win_rate": parse_number(stats.get("Win Rate")),
        "loss_rate": parse_number(stats.get("Loss Rate")),
        "orders": int(parse_number(stats.get("Total Orders"))),
    }


def run_backtest(
    repo_root: Path,
    lean_cmd: str,
    strategy_dir: str,
    symbols: tuple[str, ...],
    output_dir: Path,
    no_update: bool,
) -> SweepResult:
    output_dir.mkdir(parents=True, exist_ok=True)
    backtest_name = combo_label(symbols)
    cmd = [
        lean_cmd,
        "backtest",
        strategy_dir,
        "--parameter",
        "symbols",
        ",".join(symbols),
        "--backtest-name",
        backtest_name,
        "--output",
        str(output_dir),
    ]
    if no_update:
        cmd.append("--no-update")

    run = subprocess.run(
        cmd,
        cwd=repo_root,
        text=True,
        capture_output=True,
    )

    summary_path = find_summary_file(output_dir)
    if run.returncode != 0:
        return SweepResult(
            symbols=symbols,
            size=len(symbols),
            backtest_name=backtest_name,
            output_dir=output_dir,
            summary_path=summary_path,
            sharpe=0.0,
            net_profit=0.0,
            drawdown=0.0,
            fees=0.0,
            win_rate=0.0,
            loss_rate=0.0,
            orders=0,
            status="failed",
            error=run.stderr.strip() or run.stdout.strip(),
        )

    if summary_path is None:
        return SweepResult(
            symbols=symbols,
            size=len(symbols),
            backtest_name=backtest_name,
            output_dir=output_dir,
            summary_path=None,
            sharpe=0.0,
            net_profit=0.0,
            drawdown=0.0,
            fees=0.0,
            win_rate=0.0,
            loss_rate=0.0,
            orders=0,
            status="failed",
            error="No summary.json file was found in the output directory.",
        )

    summary = load_summary(summary_path)
    stats = extract_stats(summary)
    return SweepResult(
        symbols=symbols,
        size=len(symbols),
        backtest_name=backtest_name,
        output_dir=output_dir,
        summary_path=summary_path,
        sharpe=stats["sharpe"],
        net_profit=stats["net_profit"],
        drawdown=stats["drawdown"],
        fees=stats["fees"],
        win_rate=stats["win_rate"],
        loss_rate=stats["loss_rate"],
        orders=stats["orders"],
        status="ok",
    )


def format_table(results: list[SweepResult], top_n: int) -> str:
    headers = [
        "rank",
        "symbols",
        "size",
        "sharpe",
        "net_profit",
        "drawdown",
        "fees",
        "win_rate",
        "loss_rate",
        "orders",
        "status",
    ]
    rows = []
    for index, result in enumerate(results[:top_n], start=1):
        rows.append(
            [
                index,
                ",".join(result.symbols),
                result.size,
                f"{result.sharpe:.4f}",
                f"{result.net_profit:.4f}",
                f"{result.drawdown:.4f}",
                f"{result.fees:.2f}",
                f"{result.win_rate:.2f}",
                f"{result.loss_rate:.2f}",
                result.orders,
                result.status,
            ]
        )

    widths = [len(header) for header in headers]
    for row in rows:
        for index, cell in enumerate(row):
            widths[index] = max(widths[index], len(str(cell)))

    lines = []
    lines.append(" | ".join(header.ljust(widths[index]) for index, header in enumerate(headers)))
    lines.append("-+-".join("-" * width for width in widths))
    for row in rows:
        lines.append(" | ".join(str(cell).ljust(widths[index]) for index, cell in enumerate(row)))
    return "\n".join(lines)


def main() -> int:
    args = parse_args()
    repo_root = Path(__file__).resolve().parents[1]
    strategy_dir = args.strategy
    symbols = flatten_symbols(args.symbols)
    sizes = sorted({size for size in args.sizes if size > 0})

    if len(symbols) < 2:
        print("Need at least two symbols to sweep.", file=sys.stderr)
        return 1

    if not sizes:
        print("Need at least one valid combination size.", file=sys.stderr)
        return 1

    timestamp = dt.datetime.now().strftime("%Y%m%d_%H%M%S")
    strategy_name = Path(strategy_dir).name
    output_root = Path(args.output_root) if args.output_root else (
        repo_root / "sweep-results" / strategy_name / timestamp
    )
    output_root.mkdir(parents=True, exist_ok=True)

    all_results: list[SweepResult] = []
    all_combos: list[tuple[str, ...]] = []
    for size in sizes:
        if size > len(symbols):
            continue
        all_combos.extend(itertools.combinations(symbols, size))

    if not all_combos:
        print("No combinations to run for the requested sizes.", file=sys.stderr)
        return 1

    print(f"Running {len(all_combos)} combinations across sizes: {', '.join(map(str, sizes))}")
    print(f"Output root: {output_root}")

    for index, combo in enumerate(all_combos, start=1):
        combo_dir = output_root / combo_label(combo)
        print(f"[{index}/{len(all_combos)}] {','.join(combo)}")
        if args.dry_run:
            print(f"  lean backtest {strategy_dir} --parameter symbols {','.join(combo)} --backtest-name {combo_label(combo)} --output {combo_dir}")
            continue

        result = run_backtest(
            repo_root=repo_root,
            lean_cmd=args.lean,
            strategy_dir=strategy_dir,
            symbols=combo,
            output_dir=combo_dir,
            no_update=args.no_update,
        )
        all_results.append(result)
        if result.status == "ok":
            print(
                f"  sharpe={result.sharpe:.4f} net={result.net_profit:.4f} drawdown={result.drawdown:.4f} fees={result.fees:.2f} orders={result.orders}"
            )
        else:
            print(f"  FAILED: {result.error}")

    if args.dry_run:
        return 0

    ranked = sorted(
        (result for result in all_results if result.status == "ok"),
        key=lambda result: (-result.sharpe, -result.net_profit, result.drawdown, result.fees),
    )
    failed = [result for result in all_results if result.status != "ok"]

    results_json = output_root / "results.json"
    with results_json.open("w", encoding="utf-8") as handle:
        json.dump(
            [
                {
                    "symbols": list(result.symbols),
                    "size": result.size,
                    "sharpe": result.sharpe,
                    "net_profit": result.net_profit,
                    "drawdown": result.drawdown,
                    "fees": result.fees,
                    "win_rate": result.win_rate,
                    "loss_rate": result.loss_rate,
                    "orders": result.orders,
                    "status": result.status,
                    "backtest_name": result.backtest_name,
                    "summary_path": str(result.summary_path) if result.summary_path else None,
                }
                for result in all_results
            ],
            handle,
            indent=2,
        )

    print("\nRanked results:")
    print(format_table(ranked, args.top))
    print(f"\nSaved raw results to: {results_json}")
    if failed:
        print(f"\nFailed runs: {len(failed)}")
        for result in failed:
            print(f"  {','.join(result.symbols)} -> {result.error[:200]}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())