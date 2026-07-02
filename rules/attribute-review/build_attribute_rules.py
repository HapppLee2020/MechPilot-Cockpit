#!/usr/bin/env python3
"""CKP-004-16: Build machine-executable attribute rules from agg_cache.json.

Reads agg_cache.json (per-material aggregation) and emits
attribute_rules.generated.json — a compact, deterministic rule set that the
Cockpit property-workbench can load for local pre-check of attribute reviews.

Confidence policy:
  total >= 100          -> "strong"
  20 <= total < 100     -> "medium"
  1  <= total < 20      -> "low"
  total == 0            -> "reference_only"  (candidate only, no hard check)

Usage:
  python build_attribute_rules.py            # uses sibling agg_cache.json
  python build_attribute_rules.py path.json  # custom cache path

Re-runnable: output is regenerated from cache each time; no side state.
"""
from __future__ import annotations

import json
import os
import sys
from datetime import datetime, timezone

SCHEMA_VERSION = "mechpilot.attribute_rules.v1"
STRONG_MIN = 100
MEDIUM_MIN = 20
LOW_MIN = 1


def classify_confidence(total: int) -> str:
    if total >= STRONG_MIN:
        return "strong"
    if total >= MEDIUM_MIN:
        return "medium"
    if total >= LOW_MIN:
        return "low"
    return "reference_only"


def _ratio(count: int, total: int) -> float:
    if total <= 0:
        return 0.0
    return round(count / total, 4)


def build_material_rule(name: str, agg: dict) -> dict:
    total = int(agg.get("total", 0) or 0)
    treats = agg.get("treats", []) or []   # [[value, count], ...]
    cats = agg.get("cats", []) or []
    has_spec = int(agg.get("has_spec", 0) or 0)
    has_brand = int(agg.get("has_brand", 0) or 0)

    surface_treatments = []
    for pair in sorted(treats, key=lambda p: p[1] if len(p) > 1 else 0, reverse=True):
        if not isinstance(pair, list) or len(pair) < 1:
            continue
        val = str(pair[0]) if pair[0] is not None else ""
        cnt = int(pair[1]) if len(pair) > 1 and pair[1] else 0
        if not val:
            continue
        surface_treatments.append({
            "value": val,
            "count": cnt,
            "ratio": _ratio(cnt, total),
        })

    primary_categories = []
    for pair in sorted(cats, key=lambda p: p[1] if len(p) > 1 else 0, reverse=True):
        if not isinstance(pair, list) or len(pair) < 1:
            continue
        val = str(pair[0]) if pair[0] is not None else ""
        cnt = int(pair[1]) if len(pair) > 1 and pair[1] else 0
        if not val:
            continue
        primary_categories.append({
            "value": val,
            "count": cnt,
            "ratio": _ratio(cnt, total),
        })

    recommended = surface_treatments[0]["value"] if surface_treatments else ""
    allowed = [st["value"] for st in surface_treatments]

    return {
        "material": name,
        "sample_count": total,
        "confidence": classify_confidence(total),
        "primary_categories": primary_categories,
        "surface_treatments": surface_treatments,
        "recommended_surface_treatment": recommended,
        "allowed_surface_treatments": allowed,
        "spec_fill_rate": _ratio(has_spec, total),
        "brand_fill_rate": _ratio(has_brand, total),
    }


def build_rules(cache_path: str) -> dict:
    with open(cache_path, "r", encoding="utf-8") as f:
        cache = json.load(f)

    if not isinstance(cache, dict):
        raise ValueError("agg_cache.json must be a JSON object keyed by material name")

    materials = {}
    for name, agg in cache.items():
        if not isinstance(agg, dict):
            continue
        materials[name] = build_material_rule(name, agg)

    materials = dict(sorted(materials.items(), key=lambda kv: kv[1]["sample_count"], reverse=True))

    return {
        "schema_version": SCHEMA_VERSION,
        "generated_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "source": {
            "manual": "PDT_Complete_Attribute_Specification.md",
            "cache": "agg_cache.json",
        },
        "confidence_policy": {
            "strong_min_total": STRONG_MIN,
            "medium_min_total": MEDIUM_MIN,
            "low_min_total": LOW_MIN,
            "reference_only": "total == 0",
        },
        "material_count": len(materials),
        "materials": materials,
    }


def main(argv: list[str]) -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    cache_path = argv[1] if len(argv) > 1 else os.path.join(here, "agg_cache.json")
    out_path = os.path.join(here, "attribute_rules.generated.json")

    if not os.path.exists(cache_path):
        print(f"[FAIL] cache not found: {cache_path}", file=sys.stderr)
        return 2

    rules = build_rules(cache_path)

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(rules, f, ensure_ascii=False, indent=2)

    conf_counts: dict[str, int] = {}
    for m in rules["materials"].values():
        conf_counts[m["confidence"]] = conf_counts.get(m["confidence"], 0) + 1

    print(f"[OK] wrote {out_path}")
    print(f"     schema_version : {rules['schema_version']}")
    print(f"     material_count : {rules['material_count']}")
    print(f"     confidence     : {conf_counts}")
    for check in ("SUS304", "6061-T6", "Q235-A", "45#"):
        if check in rules["materials"]:
            m = rules["materials"][check]
            print(f"     {check:<10}: total={m['sample_count']} conf={m['confidence']} "
                  f"reco='{m['recommended_surface_treatment']}' treats={len(m['surface_treatments'])}")
        else:
            print(f"     [WARN] {check} missing")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
