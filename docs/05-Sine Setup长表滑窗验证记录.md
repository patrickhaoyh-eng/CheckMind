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

## 8. Profile Editor 翻页回归专项补充

### 8.1 现象
- `Profile Editor` 子窗口在正式业务链路中一度出现“翻页失效 / 截图不稳定 / 偶发触发 Testlab `EXCEPTION_ACCESS_VIOLATION`”。
- 该问题不是主窗口 `Channel Setup` / `Sine Setup` 长表扫描退化，而是 `Profile Editor` 子窗口分页链的专项回归。

### 8.2 最终验证基线
- 前置重标 run：`artifacts/probe-runs/5a823e1a18c54854ad772ccc1b258b93`
- 正式业务 run：`artifacts/probe-runs/8f91b14758d34f42ae6ddb8564d26cfd`
- 回顶策略修正后正式业务验证 run：
  - `artifacts/probe-runs/718dc8781c074e79ae8aed839ef19f7d`
- 关键结构化证据：
  - `artifacts/probe-runs/8f91b14758d34f42ae6ddb8564d26cfd/testlab_phases.log`
  - `artifacts/probe-runs/8f91b14758d34f42ae6ddb8564d26cfd/testlab_run.json`
  - `artifacts/probe-runs/8f91b14758d34f42ae6ddb8564d26cfd/results.json`
  - `artifacts/probe-runs/718dc8781c074e79ae8aed839ef19f7d/testlab_phases.log`
  - `artifacts/probe-runs/718dc8781c074e79ae8aed839ef19f7d/table_scroll_events_profileeditortablescan.json`
- 关键图片证据：
  - `artifacts/probe-runs/8f91b14758d34f42ae6ddb8564d26cfd/screenshots/evidence/testlab_table_profileeditortablescan_v_00.png`
  - `artifacts/probe-runs/8f91b14758d34f42ae6ddb8564d26cfd/screenshots/evidence/testlab_table_profileeditortablescan_v_01.png`
  - `artifacts/probe-runs/8f91b14758d34f42ae6ddb8564d26cfd/screenshots/evidence/testlab_table_profileeditortablescan_v_02.png`
  - `artifacts/probe-runs/8f91b14758d34f42ae6ddb8564d26cfd/screenshots/evidence/testlab_table_profileeditortablescan_stitched.png`

### 8.3 已验证事实
- `testlab_run.json` 已写出 `Profile Editor -> TableScans -> Chunks=3`，且 `UniqueChunkCount=3`，说明正式链路下确实形成了“首屏 + 两次翻页”三屏证据，而不是只截第一页。
- `testlab_phases.log` 中 `runner.profile_editor_table_scan_begin` 已明确记录 `maxSteps=3`，对应当前业务口径“回顶后翻两次”。
- 同一 run 中 `runner.profile_editor_post_pgdn_detect1_done / detect2_changed` 连续命中，且最终三个 chunk 的 `SerialSha256` 分别不同，说明两次 `PgDn` 均产生了新的列表状态。
- `results.json` 为 `status=completed`，现场也已确认“翻页正常，截图正常，无报错”。
- 在修正回顶策略后，正式业务 run `718dc8781c074e79ae8aed839ef19f7d` 中已明确记录：
  - `runner.table_reset_top_bootstrap_pgdn | serialChanged=1`
  - 随后 `runner.table_reset_top | serialChanged=True; stableTop=1`
- 同一 run 的 `table_scroll_events_profileeditortablescan.json` 显示正式翻页阶段连续两次 `PgDn` 均为 `Changed=true`，说明“先激活翻页语义，再回顶，再正式翻页”的链路已经独立复跑通过。

### 8.4 根因结论
- 根因 1：`Profile Editor` 不能继续依赖默认推导的分页点击点。默认点位可能落入可编辑文本区，单击后进入编辑态，再执行 `PgDn` 会把 Testlab 推入不稳定状态。
- 根因 2：仅修正点击点还不够。`Profile Editor` 还必须纳入 deterministic top reset；否则扫描可能从残留页状态开始，导致同一套配置偶发“这次能翻、下次失效”。
- 根因 3：`Profile Editor` 最大化后并不是立即处于“可用 `PgUp` 回顶”的翻页语义。现场验证已证明，直接 `PgUp` 只会无效或停留在页内选中语义，需要先执行一次有效 `PgDn`，再 `PgUp` 才能回到真正可放行的顶部。
- 因此当前稳定方案不是单点修补，而是三个条件同时成立：
  - 使用人工标定的空白分页点：`PagingFocusPointWindow` / `PagingActivationPointWindow`
  - 在正式扫描前执行 deterministic top reset
  - 对 `Profile Editor` 先执行一次 bootstrap `PgDn` 激活翻页语义，再连续 `PgUp` 回顶，最后进入正式两次 `PgDn`

### 8.5 为什么这次修复有效
- 修复后，`Profile Editor` 的分页前置动作不再进入可编辑单元格，而是稳定落在空白区域；因此 `PgDn` 作用于列表翻页，而不是文本编辑态。
- 同时，回顶前新增的 bootstrap `PgDn` 把控件从“页内选中/焦点导航语义”切换到“可翻页语义”，随后 `PgUp` 回顶才会稳定生效。
- 因而 `Profile Editor` 的当前固定流程已更新为：bootstrap `PgDn` -> `PgUp` 回顶 -> 首屏 -> `PgDn` -> 第二屏 -> `PgDn` -> 第三屏。
- 当前结论只覆盖本工位、当前人工标定 profile 和当前窗口布局；若后续列表布局、行高、列宽或分页空白点漂移，仍应通过 preflight / 重标阻断，而不是假定运行时自适应。

## 9. Notch Profiles 第二刀门禁专项补充

### 9.1 目标
- 给 `Define notch profiles` 增加第二刀 preflight 门禁，确保正式链路在进入 `Notch Profile` 编辑窗口前，至少已经确认页面在位、列表 ROI 可采集、入口流程可执行。
- 当前正式口径已从“内容级硬签名阻断”调整为“几何/流程门禁放行 + 哈希漂移记证不阻断”，因为左侧列表条目数量与名称属于动态业务内容，不适合作为硬性布局签名。
- 本专项只记录已经有证据支撑的“布局签名标定、运行时误报、门禁口径调整与正式放行”事实，不把后续多序号扩展验证提前写成已完成。

### 9.2 标定与正式验证基线
- 布局签名标定 run：`artifacts/probe-runs/a53d4754b9d541ddb710f72ec0d4a910`
- 误报 run（首行选中态导致假阳性 `mismatch`）：`artifacts/probe-runs/56fd1765a18b42028b8c33b5f2a9227e`
- 修正后正式验证 run：`artifacts/probe-runs/f7e209f91e1845f78fed03d45b64ed3a`
- 关键结构化证据：
  - `artifacts/probe-runs/a53d4754b9d541ddb710f72ec0d4a910/testlab_phases.log`
  - `artifacts/probe-runs/56fd1765a18b42028b8c33b5f2a9227e/define_notch_profiles_layout_preflight_report.json`
  - `artifacts/probe-runs/f7e209f91e1845f78fed03d45b64ed3a/testlab_phases.log`
  - `artifacts/probe-runs/f7e209f91e1845f78fed03d45b64ed3a/results.json`
- 关键图片证据：
  - `artifacts/probe-runs/a53d4754b9d541ddb710f72ec0d4a910/screenshots/evidence/define_notch_profiles_layout_signature_verify.png`
  - `artifacts/probe-runs/56fd1765a18b42028b8c33b5f2a9227e/screenshots/evidence/define_notch_profiles_layout_signature_actual.png`

### 9.3 已验证事实
- 标定链路已打通。标定 run `a53d4754b9d541ddb710f72ec0d4a910` 中已明确记录：
  - `calib.child_window.define_notch_profiles_layout_signature_confirmed`
  - `calib.child_window.define_notch_profiles_layout_signature_capture_ok`
  - `calib.child_window.define_notch_profiles_layout_signature_written`
- `workstation_profile.json` 中已成功写入 `define_notch_profiles.VerifyTargets.notch_profiles_layout`，说明第二刀门禁的配置前提已具备。
- 运行时误报的根因已收敛。误报 run `56fd1765a18b42028b8c33b5f2a9227e` 的实际图与标定图在页面结构上保持一致：
  - 标题、列头、列表项 `30g / 60g`、以及底部 `Edit / Remove / Duplicate / Import... / Export...` 按钮区都一致
  - 真正不同的是第一行的选中渲染：基线图表现为整格蓝底，运行时图表现为白底加蓝色焦点框
- 因此，初版 `notch_profiles_layout` 的 `mismatch` 不是“页面真的漂移”，而是签名 ROI 把首行选中态也纳入了比对，导致 UI 状态差异被误判成布局漂移。

### 9.4 修正结论
- `notch_profiles_layout` 的签名 ROI 已从“整块左侧列表 + 按钮区”逐步收窄为底部控制带，并最终排除了：
  - 左侧动态列表条目文本区
  - 首行/当前行选中态差异
  - 右侧竖向滚动条滑块状态
- 运行期进一步证明：即使只保留底部控制带，`Define notch profiles` 的内容仍可能因按钮启用态等运行时状态变化而出现哈希漂移。
- 因而当前正式修正不是继续追求“硬签名完全一致”，而是把第二刀门禁调整为：
  - 页面在位与 ROI 可采集则判定几何/流程门禁通过
  - 哈希漂移写入 `define_notch_profiles_layout_hash_warning.json` 与 phase marker，作为证据保留
  - 不再因该类漂移阻断后续 `Notch Profile` 正式业务链路

### 9.5 正式验证结论
- 旧口径下，单序号 run `f7e209f91e1845f78fed03d45b64ed3a` 曾确认 `runner.notch_profiles_layout_signature_matched`，但后续扩展到真实 `row=3` 内容时暴露出“动态列表内容不适合做硬签名门禁”的设计缺陷。
- 新口径下，正式 run `a0f15376086c46fda4d7de43943d6b0e` 已明确记录：
  - `runner.notch_profiles_layout_signature_probe`（该 marker 仅用于当轮定位 `pixel hash / png bytes hash` 差异，后续已从长期运行日志中移除）
  - `runner.notch_profiles_layout_hash_mismatch_ignored`
  - `runner.notch_profiles_layout_geometry_verified`
  - `runner.notch_profile_scan_begin | row=1/2/3`
  - `runner.notch_profile_scan_completed | row=1/2/3;chunks=3;unique=3;closed=1`
- 同一 run 的 `results.json` 记录：
  - `status = completed`
  - `completedNotchProfileCount = 3`
  - `requestedNotchProfileCount = 3`
  - `alerts = []`
- 这说明当前工位、当前 profile、当前请求范围下：
  - `Channel safety parameters` 恢复链已不再是主阻断
  - `Define notch profiles` 第二刀门禁已按“几何/流程放行、哈希漂移留证”的新口径工作
  - `Notch Profile #1/#2/#3` 正式业务链路已恢复闭环

### 9.6 当前边界
- 上述结论已覆盖“单序号正式链路 + 第二刀门禁不再假阳性阻断”。
- 但尚未覆盖“多序号批量执行”的扩展验证；后续若要验证 `NotchProfileIndexes='1,2,...'`，应另做 smoke 留档，不把本轮单序号成功外推为全部序号已验证。

### 9.7 模板口径扩展复核
- 模板口径 `notchProfileCount=2` 已形成正式验证基线：run `e2aab77a4ffe4b3ab5c9ca7eab273f2c` 已确认当任务通过模板口径而非显式 `Indexes='1,2'` 驱动时，运行器会先执行 `runner.notch_profiles_layout_selection_normalized`，随后通过第二刀门禁，最终写出 `completedNotchProfileCount=2 / requestedNotchProfileCount=2`。
- 在“3 条真实内容已补齐”之后，run `a0f15376086c46fda4d7de43943d6b0e` 已确认 `notchProfileCount=3` 在新门禁口径下可直接完成 `row=1/2/3` 三个子窗口扫描，最终写出 `completedNotchProfileCount=3 / requestedNotchProfileCount=3 / alerts=[]`。
- 更大的 mismatch smoke `notchProfileCount=4` 也已在同一新门禁口径下完成复核：run `860d5a4d37bc47378064fd8f07870528` 中，第二刀门禁先命中 `runner.notch_profiles_layout_geometry_verified`，随后 `row=1/2/3` 均完成 `UniqueChunkCount=3` 的扫描与关闭返回，最后才在 `row=4` 进入 `runner.notch_profile_count_mismatch | requested=4;completed=3;failedRow=4`。
- 同一 run 的 `results.json` 已写出：
  - `status = completed_with_warning`
  - `resultCode = NOTCH_PROFILE_COUNT_MISMATCH`
  - `completedNotchProfileCount = 3`
  - `requestedNotchProfileCount = 4`
  - `failedRow = 4`
- 这说明当前“数量不符提示口径”没有被新门禁破坏；第二刀门禁先完成页面在位与流程可执行校验，再把 mismatch 精确定位到真实缺失的第 4 条，而不会被左侧动态列表内容提前误拦。
- 显式索引路径 `NotchProfileIndexes='1,2,3,4'` 也已在同一新门禁口径下完成复核：run `8b3044c5ffa746748b61709afcf0b243` 中，`row=1/2/3` 三个子窗口均完成扫描，`row=4` 时写出 `runner.notch_profile_count_mismatch | requested=4;completed=3;failedRow=4;mode=repeated_selection;input=Notch Profile Indexes`；`results.json` 同步写出 `message=...请检查 Notch Profile Indexes 是否填写正确。`
- 显式索引成功路径 `NotchProfileIndexes='1,2,3'` 也已在同一新门禁口径下完成正式验证：run `756a6913f4794edcb4eceda885762a75` 中，第二刀门禁先命中 `runner.notch_profiles_layout_geometry_verified`，随后 `row=1/2/3` 三个子窗口均完成 `UniqueChunkCount=3` 的扫描与关闭返回；`results.json` 写出 `status=completed / completedNotchProfileCount=3 / alerts=[]`，`testlab_run.json` 中 `NotchProfileCountMismatch = null`。
