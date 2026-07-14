# 翻页滑窗取证范式（PgUp / PgDn）

> 状态说明：本文档保留为阶段性执行范式/历史成果。
> 当前长期主基线已切换为：`docs/08-人工标定统一基线.md`。
> `PgUp / PgDn` 仍可作为执行动作保留，但不再单独代表长期设计基线。

## 1. 目的
- 为所有“需要翻页的页面/表格”提供统一的、可复用的滑窗取证范式，避免每个页面各写一套步进逻辑导致漏屏与不可控。
- 彻底抛弃 OCR 作为主链路判定手段，避免慢与不确定性。

## 2. 适用范围
- 适用：Testlab 内所有存在纵向滚动/翻页的长表/长表单，包括但不限于：
  - `Channel Setup` 页面
  - `Sine Setup` 页面
- 不适用：无需翻页即可完整覆盖的短表（可直接单屏取证）。

## 3. 总体原则
- 主路径只使用：
  - 回顶：`PgUp`
  - 下翻：`PgDn`
- 证据节奏：
  - 每次有效翻页后必须立刻保存一屏正式证据图（v_XX / window_v_XX / serial_v_XX）
  - 通过“变化检测”与“序号列覆盖”证明不漏屏
- 门禁（Fail-fast）：
  - 无法证明回到顶部稳定态时，不允许进入向下扫描
- 性能目标（体感）：
  - 回顶动作必须快，避免给现场“很慢”的印象

## 4. 回顶（PgUp 主路径）

### 4.1 回顶步骤
1) 聚焦到表格交互区域（确保 PgUp 作用在表格滚动容器上）
2) 执行 `PgUp × 10`
3) 采样并计算状态 hash：
   - `serialSha`：序号列 ROI 的 sha256
   - `scrollbarSha`：滚动条条带 ROI 的 sha256
4) 若未达到“连续 2 次不变”，执行一次追加尝试：
   - `PgUp × 5`
   - 再次判定“连续 2 次不变”
5) 若仍不稳定：fail-fast 阻断本次扫描，并输出可行动提示

### 4.2 回顶稳定判定（门禁）
- 目标：证明已经到达顶部稳定态，而不是“猜测已到顶”。
- 判定口径：状态 hash 连续 2 次采样一致，即：
  - `serialSha + scrollbarSha` 连续 2 次相等
- 注意：这里的 hash 是“稳定性判定”，不是“固定签名匹配”：
  - 不要求跨电脑/跨界面永远相同
  - 只要求同一次运行中进入稳定态

### 4.3 回顶失败时的可行动提示（示例）
- 请确认焦点在表格内（表格内部任意单元格/空白处点一下再重试）
- 请确认当前工位符合固定工位门禁（分辨率/DPI/窗口位置/最大化）
- 如控件不响应 PgUp/PgDn，需单独记录为控件兼容性问题并阻断

## 5. 下翻扫描（PgDn-only）

### 5.1 下翻步骤
1) 先截取首屏正式证据 `v_00`（并保存整窗/序号列）
2) 循环执行：
   - `PgDn × 1`
   - 立刻截取下一屏正式证据 `v_{k+1}`
   - 做变化检测（serial+scrollbar stateHash）：
     - 未变化：停止（到达表尾或控件不再翻页）
     - 有变化：继续下一步

### 5.2 停止条件
- 连续一次 PgDn 后 `serialSha + scrollbarSha` 不再变化，判定本次扫描结束。

## 6. 可配置项（环境变量）
- 目标：允许不同页面/控件做微调，但默认值必须是“极快值”，满足现场体感。
- 建议项（名称以实现为准）：
  - `CHECKMIND_TABLE_RESET_TOP_PGUP_COUNT`（默认 10）
  - `CHECKMIND_TABLE_RESET_TOP_PGUP_RETRY_COUNT`（默认 5）
  - `CHECKMIND_TABLE_RESET_TOP_STABLE_CONSECUTIVE`（默认 2）
  - `CHECKMIND_TABLE_KEY_DELAY_MS`（默认极小值，用于保证按键不被吞）

## 7. 证据产物要求
- 每个页面的每个表格扫描必须产出：
  - `coverage.json`（整轮汇总，包含双页/多页）
  - `table_scroll_events_{table}.json`
  - `screenshots/testlab_table_{table}_v_XX.png`
  - `screenshots/testlab_table_{table}_window_v_XX.png`
  - `screenshots/testlab_table_{table}_serial_v_XX.png`
  - `screenshots/testlab_table_{table}_stitched.png`（可选，但建议保留用于快速复核）

## 8. 非目标（明确不做）
- 不使用 OCR 参与回顶判定/翻页判定/变化检测。
- 不在主路径中混用 wheel/drag 作为翻页步进兜底（除非另开任务并写清楚验收口径）。
