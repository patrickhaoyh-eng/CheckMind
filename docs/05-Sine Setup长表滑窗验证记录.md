# Sine Setup 长表滑窗验证记录

## 1. 目的
- 固化 `Task A` 当前阶段的真实验证结果，避免后续继续开发时丢失“已经证明了什么、还没有证明什么”。
- 本记录只描述已经有证据支撑的事实，不把“下一步目标”写成“已完成结论”。

## 2. 验证对象
- 页面：`Sine Setup`
- 表格：`Channel Parameters Table`
- 验证目标：
  - 扫描前自动回顶必须可放行或可阻断
  - 正式扫描必须在每次有效滚动后立即落正式证据
  - 长表场景必须形成多屏证据，而不是只留下首屏和末屏

## 3. 证据基线
- 基线 run：`artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d`
- 关键结构化证据：
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/testlab_phases.log`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/coverage.json`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/table_scroll_events_channelparameterstable.json`
- 关键图片证据：
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/screenshots/testlab_table_channelparameterstable_v_00.png`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/screenshots/testlab_table_channelparameterstable_v_01.png`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/screenshots/testlab_table_channelparameterstable_v_02.png`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/screenshots/testlab_table_channelparameterstable_v_03.png`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/screenshots/testlab_table_channelparameterstable_window_v_00.png`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/screenshots/testlab_table_channelparameterstable_window_v_01.png`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/screenshots/testlab_table_channelparameterstable_window_v_02.png`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/screenshots/testlab_table_channelparameterstable_window_v_03.png`
  - `artifacts/probe-runs/99973c2d6d324405aa6a62edb1220a2d/screenshots/testlab_table_channelparameterstable_stitched.png`

## 4. 已验证事实

### 4.1 固定切页与进入扫描前置条件成立
- `testlab_phases.log` 记录 `runner.tab_switch_verified_by_profile_signature`，说明固定点击点切入 `Sine Setup` 后，页签验真走的是快路径，不再依赖 OCR。
- 同一 run 中记录 `runner.workstation_compliant`，说明固定工位门禁在本轮为通过状态。

### 4.2 回顶门禁已经闭环
- `testlab_phases.log` 记录：
  - `runner.table_reset_top_signature | configured=1;matched=1`
  - `runner.table_reset_top | ... stableTop=1`
- 这说明扫描前并非“假定已回顶”，而是经过顶部签名命中后才放行正式扫描。
- 若后续 run 中顶部签名不命中，则应直接阻断，不允许产出误导性长表结果。

### 4.3 正式扫描已切换为细步进滚轮主路径
- `table_scroll_events_channelparameterstable.json` 中 `Step 0/1/2` 均为：
  - `Method = wheel`
  - `WheelDelta = -120`
  - `Changed = true`
- `Step 3` 为：
  - `Method = fail`
  - `Changed = false`
- 这说明当前逻辑已经不再依赖 `PageDown` 或拖拽滚动条去制造大跳屏，而是以细步进滚轮连续推进，直到检测不到新位置才停止。

### 4.4 多屏正式证据已成立
- `coverage.json` 记录：
  - `UniqueChunkCount = 4`
  - `StitchedScreenshotPath = ...testlab_table_channelparameterstable_stitched.png`
- 同时保留了：
  - 表格局部正式图 `v_00 ~ v_03`
  - 整窗正式图 `window_v_00 ~ window_v_03`
  - 序号列正式图 `serial_v_00 ~ serial_v_03`
- 这说明当前已从“只拿到首屏和末屏”推进到“多屏连续取证 + 可拼接复核”。

## 5. 当前结论
- 可以确认：
  - 固定切页成立
  - 扫描前回顶门禁成立
  - 长表正式扫描已经能产出 4 个唯一 chunk
  - 拼接图和滚动事件日志已经形成可复核证据链
- 不能确认：
  - `18-40` 通道长表已经达到逐行级覆盖
  - 当前覆盖粒度已经足以宣称“无中间漏屏”

## 6. 当前风险与边界
- 现有证据更接近“多屏闭环已成立”，不是“密覆盖闭环已成立”。
- 从当前拼接状态看，覆盖粒度仍偏粗，首行序号大致表现为 `1 / 4 / 7 / 8` 一类跳变。
- 因此当前任务状态应保持为“进行中”，不能因为 `UniqueChunkCount = 4` 就误标为“已完成”。

## 7. 下一步最小动作
- 继续压细正式扫描步进，目标是让相邻 chunk 的覆盖更密，而不是仅增加图片数量。
- 在更长样本下复跑，确认 `UniqueChunkCount` 增长时没有引入回顶失败、重复图回退或滚动失效。
- 当覆盖密度达到验收要求后，再把 `Task A` 从“主体链路跑通”升级为“覆盖密度达标”。
