# Attribute Review Rules Package (CKP-004-16)

> **来源**：PDT 属性规则资料（`D:\Hermes\workspace\2026-06\30`）  
> **生成时间**：2026-07-01  
> **状态**：MVP — 机器可执行规则 + 本地预检骨架

---

## 一、目录结构

```
rules/attribute-review/
├── PDT_Complete_Attribute_Specification.md   ← 人可读规则手册（从 Hermes 工作区复制）
├── agg_cache.json                            ← 原始聚合证据（从 Hermes 工作区复制）
├── build_attribute_rules.py                  ← 可重复运行的生成器
├── attribute_rules.generated.json            ← 机器可执行规则（生成产物）
└── README.md                                 ← 本文件
```

## 二、规则来源

| 文件 | 角色 |
|------|------|
| `PDT_Complete_Attribute_Specification.md` | 人可读手册：材质大类、表面处理建议、PDM 样本举证、历史统计解释。约 514 行。**不直接被前端解析**。 |
| `agg_cache.json` | 按材质聚合的原始证据：`total` / `treats` / `cats` / `has_spec` / `has_brand`。72 种材质。 |
| `build_attribute_rules.py` | 读取 `agg_cache.json`，生成 `attribute_rules.generated.json`。可重复运行。 |
| `attribute_rules.generated.json` | 机器可执行规则，供 Cockpit 本地预检读取。 |

## 三、生成方式

```bash
cd D:\MechPilot\Cockpit\rules\attribute-review
python build_attribute_rules.py
# 或指定自定义 cache 路径：
python build_attribute_rules.py D:\path\to\agg_cache.json
```

输出 `attribute_rules.generated.json`，覆盖旧文件。无副作用、无外部依赖（仅 Python 标准库）。

## 四、`attribute_rules.generated.json` Schema

```json
{
  "schema_version": "mechpilot.attribute_rules.v1",
  "generated_at": "2026-07-01T...Z",
  "source": { "manual": "...", "cache": "..." },
  "confidence_policy": {
    "strong_min_total": 100,
    "medium_min_total": 20,
    "low_min_total": 1,
    "reference_only": "total == 0"
  },
  "material_count": 72,
  "materials": {
    "SUS304": {
      "material": "SUS304",
      "sample_count": 2943,
      "confidence": "strong",
      "primary_categories": [
        { "value": "212 钣金件", "count": 1504, "ratio": 0.5109 }
      ],
      "surface_treatments": [
        { "value": "无", "count": 1302, "ratio": 0.4424 }
      ],
      "recommended_surface_treatment": "无",
      "allowed_surface_treatments": ["无", "表面钝化", "..."],
      "spec_fill_rate": 0.0289,
      "brand_fill_rate": 0.0234
    }
  }
}
```

### 字段含义

| 字段 | 含义 |
|------|------|
| `sample_count` | 该材质历史样本总数（来自 `agg_cache.total`） |
| `confidence` | `strong` (>=100) / `medium` (20-99) / `low` (1-19) / `reference_only` (0) |
| `primary_categories` | 历史 `W物料大类` 分布（按 count 降序） |
| `surface_treatments` | 历史表面处理分布（按 count 降序），含 `ratio` |
| `recommended_surface_treatment` | 历史最高频表面处理（先取众数） |
| `allowed_surface_treatments` | 历史出现过的所有表面处理值 |
| `spec_fill_rate` | `G规格型号` 历史填充率 = `has_spec / total` |
| `brand_fill_rate` | `P品牌` 历史填充率 = `has_brand / total` |

## 五、置信度策略

| total | confidence | 预检行为 |
|-------|-----------|---------|
| >= 100 | `strong` | 表面处理空 -> warning；不在 allowed -> warning + 推荐值 |
| 20-99 | `medium` | 同 strong，但仅 warning 不判失败 |
| 1-19 | `low` | 仅 suggestion，不强审核 |
| 0 | `reference_only` | 仅作材质候选提示，不做任何强审核 |

**重要**：统计规则不是绝对标准。`reference_only` 材质只能提示，不能判失败。

## 六、本地预检接入

Cockpit 前端 (`app.js`) 在属性审核提交前调用 `runAttributePrecheck()`：
- 读取 `attribute_rules.generated.json`（通过 C# `attribute.rules.load` 命令注入）
- 对每个选中零部件执行结构化检查
- 输出 `{ ok, issues, warnings, suggestions, evidence }`
- 结果注入 `payload.local_precheck`，随 Hermes taskContext 一起提交
- 任务队列详情面板展示预检摘要

详见 `app.js` 中 `CKP-004-16` 标记的函数。

## 七、降级策略

- 规则文件缺失 / 解析失败 -> `local_precheck.status = "unavailable"`，属性审核不阻塞，仅 warning。
- 单个材质规则缺失 -> 该材质标记 `confidence=reference_only`，仅 suggestion。

## 八、再生与版本化

- `agg_cache.json` 更新后重跑 `build_attribute_rules.py` 即可。
- `attribute_rules.generated.json` 是生成产物，可随 deploy 分发。
- `schema_version` 用于后续兼容性判断。
