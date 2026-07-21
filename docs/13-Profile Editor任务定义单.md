# Profile Editor 任务定义单

## 1. 目标

- 把当前已经在技术层面稳定的 `Profile Editor` 子窗口扫描能力，推进成一个可持续复用的业务对象闭环。
- 证明 `Profile Editor` 不只是“能翻页截图”，而是能够在 `sine` 任务链路中以稳定口径完成：
  - 进入子窗口
  - 回到真正顶部
  - 完成固定页数扫描
  - 关闭并返回父窗口
  - 写出结构化结果与证据
- 为下一阶段的字段抽取、结构化输出和规则比对预留清晰边界，但本阶段不直接展开 OCR/AI 抽取实现。

## 2. 当前已知事实

- `Profile Editor` 已完成正式实机稳定化验证：
  - run `8f91b14758d34f42ae6ddb8564d26cfd` 已确认 `TableScans.Chunks=3 / UniqueChunkCount=3`，且无 `Testlab EXCEPTION_ACCESS_VIOLATION`
  - run `718dc8781c074e79ae8aed839ef19f7d` 已确认当前固定流程 `bootstrap PgDn -> PgUp 回顶 -> 正式两次 PgDn` 可稳定执行
- `Profile Editor` 首轮任务级验收已通过：
  - run `d33468c64aad4cfbb4be0429371d28c3` 已确认在真实 `sine` 任务链路中，`Profile Editor` 完成进入子窗口、`bootstrap PgDn -> PgUp 回顶 -> 正式两次 PgDn`、`Chunks=3 / UniqueChunkCount=3 / stitched.png` 落盘、关闭返回父窗口，且未触发 `Testlab EXCEPTION_ACCESS_VIOLATION`
- 当前稳定前置依赖已存在于 `workstation_profile.json`：
  - `profile_editor` 入口点击点
  - `table_scan` ROI
  - `PagingFocusPointWindow`
  - `PagingActivationPointWindow`
  - 关闭按钮点位
- 当前 `Profile Editor` 已被接入 `Sine Setup` 任务链路，并可在 `testlab_run.json` 中落出子窗口扫描证据。

## 3. 问题陈述

- 当前 `Profile Editor` 虽已具备稳定扫描能力，但仍主要以“技术验证对象”存在。
- 对后续业务推进来说，缺的不是翻页动作本身，而是以下内容的正式定义：
  - 它在任务链路中的输入口径是什么
  - 它的证据链最小集合是什么
  - 什么叫“业务通过”
  - 什么叫“业务失败”
  - 下一步应该先补结果口径，还是先补字段抽取

## 4. V1 范围

- 纳入范围：
  - `Sine Setup` 链路中的 `Profile Editor` 子窗口进入、扫描、关闭返回
  - `testlab_run.json` 中 `Profile Editor` 结果口径收口
  - `screenshots/evidence/` 中 `Profile Editor` 拼接图和必要截图证据
  - `testlab_phases.log` 中 `Profile Editor` 关键阶段 marker 的验收口径
- 不纳入范围：
  - 不新增 OCR/AI 字段抽取
  - 不新增前端交互页面
  - 不把 `Profile Editor` 直接扩成独立任务类型
  - 不在本阶段引入新的可配置用户入参，避免把问题面从“对象闭环”扩成“任务建模”

## 5. 输入口径

### 5.1 V1 建议口径

- `Profile Editor` 在 V1 不新增独立用户输入参数。
- 它作为 `sine` 任务链路中的既有子窗口对象执行，入口仍由 `Sine Setup` 的既有运行流程触发。
- 当前执行前提沿用现有固定基线：
  - 工位与 `workstation_profile.json` 一致
  - `profile_editor.table_scan` ROI 与分页点位已完成标定
  - `Sine Setup` 可正常进入并满足 preflight 门禁

### 5.2 调试兼容口径

- 若后续需要做专项回归，可继续复用当前内部调试入口和现有 `sine` 正式链路。
- 但在本任务定义单 V1 中，不新增新的外部模板字段，也不承诺新的 CLI 参数。

## 6. 输出与证据链

### 6.1 结构化输出

- `results.json`
  - 主任务整体成功/失败不应因 `Profile Editor` 证据缺失而被“静默掩盖”
- `testlab_run.json`
  - 必须能定位到 `Profile Editor` 对应的子窗口结果
  - 至少包含：
    - `ProfileEditorFormalResult`
    - `ProfileEditorFormalResult.WindowOpened`
    - `ProfileEditorFormalResult.ChunkCount`
    - `ProfileEditorFormalResult.UniqueChunkCount`
    - `ProfileEditorFormalResult.StitchedScreenshotPath`
    - `ProfileEditorFormalResult.FinalCompareScreenshotPath`
    - `ProfileEditorFormalResult.ChildWindowClosed`
    - `ProfileEditorFormalResult.ReturnedToParent`
    - `ProfileEditorFormalResult.FlowVerified`
    - `FinalCompareArtifacts`
- `testlab_phases.log`
  - 至少出现：
    - 子窗口打开
    - 表格扫描开始
    - 回顶完成或允许放行
    - 扫描完成
    - 子窗口关闭返回

### 6.2 可视化证据

- 至少保留以下证据：
  - `screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png`
  - `screenshots/evidence/` 中保留原始 `stitched.png`
  - `screenshots/evidence/` 与 `screenshots/debug/` 中继续保留必要的 chunk / 调试截图
  - 如发生失败，保留失败前最后一屏截图
- 若出现翻页异常、回顶异常或疑似进入文本编辑态，必须留下可复盘证据，而不是只报一句“翻页失败”。

### 6.3 证据口径要求

- 证据必须能回答以下问题：
  - 是否真的进入了 `Profile Editor`
  - 是否真的执行了 `bootstrap PgDn -> PgUp 回顶`
  - 是否真的产出了 3 屏扫描证据
  - 是否真的关闭子窗口并返回父窗口

## 7. 成功判据

- 以下条件同时满足，才算 `Profile Editor` V1 业务闭环通过：
  - `Profile Editor` 在 `sine` 任务链路中被正常打开
  - 回顶与翻页链路按当前固定口径执行
  - `testlab_run.json.ProfileEditorFormalResult.ChunkCount=3`
  - `testlab_run.json.ProfileEditorFormalResult.UniqueChunkCount=3`
  - `testlab_run.json.ProfileEditorFormalResult.FinalCompareScreenshotPath` 已正常落盘
  - `testlab_run.json.ProfileEditorFormalResult.ChildWindowClosed=true`
  - `testlab_run.json.ProfileEditorFormalResult.ReturnedToParent=true`
  - `testlab_run.json.ProfileEditorFormalResult.FlowVerified=true`
  - 返回父窗口后，后续任务链路不被破坏
  - 无 `Testlab EXCEPTION_ACCESS_VIOLATION`

## 8. 失败判据

- 出现以下任一情况，即判当前对象闭环失败：
  - 未能进入 `Profile Editor`
  - 进入后点击落入文本编辑态，导致分页语义失效
  - 未执行 `bootstrap PgDn` 就直接尝试 `PgUp`，导致伪回顶
  - `Chunks < 3`
  - `UniqueChunkCount < 3`
  - 未产出 `stitched.png`
  - 子窗口未关闭或关闭后未返回父窗口
  - 因 `Profile Editor` 扫描导致主链路异常中断或 Testlab 崩溃

## 9. 最小实施步骤

1. 先固定 `Profile Editor` 的 V1 业务口径：
   - 只做“稳定扫描并留档”
   - 不在本阶段展开字段抽取
2. 收口当前 `testlab_run.json` 中 `Profile Editor` 的结构化结果字段：
   - 明确哪些字段作为正式验收字段
   - 明确哪些字段仍属于调试辅助字段
3. 选一条标准任务级回归命令作为 `Profile Editor` V1 主验证入口：
   - 优先复用当前已通过的 `sine` 正式 run
4. 基于该命令补一轮独立的 `Profile Editor` 任务级证据留档：
   - `results.json`
   - `testlab_run.json`
   - `testlab_phases.log`
   - `Profile Editor` 拼接图
5. 在文档中固化 `Profile Editor` 的正式基线 run 与通过/失败口径

## 10. 推荐首轮验收 run

- 推荐先复用已知稳定 run 作为初始基线对照：
  - `8f91b14758d34f42ae6ddb8564d26cfd`
  - `718dc8781c074e79ae8aed839ef19f7d`
- 当前已补齐“任务级、非专项”的 `Profile Editor` 独立验收 run：
  - `d33468c64aad4cfbb4be0429371d28c3`

## 11. 下一步建议

- 当前最小下一步不是继续重复同类验收，而是根据任务级通过结果，决定：
  - 是否收口 `testlab_run.json` 中 `Profile Editor` 的正式结果字段
  - 是否进入下一阶段结构化输出设计

## 12. V2 目标与边界

- `Profile Editor` V2 的目标，不是直接跳到“AI 自动判定”，而是先把它推进成一个可被后续抽取、归一化和规则比对稳定消费的业务字段契约对象。
- V2 当前只做“字段契约定义”，不直接展开 OCR/AI 实现，不修改主运行链路，不扩成新的任务类型。
- V2 仍然复用当前已成立的 V1 流程核验前提：
  - 已真正进入 `Profile Editor`
  - 已真正完成 `bootstrap PgDn -> PgUp 回顶 -> 正式两次 PgDn`
  - 已真正形成 `ChunkCount=3 / UniqueChunkCount=3`
  - 已真正产出 `screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png`
  - 已真正关闭子窗口并返回父窗口
- V2 的输出定位：
  - 给后续字段抽取提供正式契约
  - 给后续比对/告警提供可追溯证据链
  - 给后续 AI 参与抽取提供清晰边界，但不允许 AI 在无证据条件下直接产出高等级结论

## 13. V2 当前假设

- 当前前 9 列已基于真实样图与用户填写的 Excel 样板完成第一版正式字段映射，不再回退成纯 `col_XX` 占位口径。
- 当前唯一可稳定依赖的正式输入，是 `screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png` 及其对应的原始 `evidence/` / `debug/` 留档。
- 对于后续新增列或仍未确认的列，V2 继续先走“中间层字段契约”路径，即：
  - 行级对象
  - 列级对象
  - 原始文本
  - 归一化文本
  - 单位
  - 置信度
  - `evidenceRef`
- 任何后续新增业务字段名，必须来自：
  - 用户确认的字段映射表
  - 样表实物证据
  - 或可核对的既有模板/规范
- 若新增列样本不足，V2 应输出“待映射/待确认”，而不是擅自补全不存在的字段名。

## 14. V2 正式字段契约

### 14.1 顶层对象

- 建议先定义 `ProfileEditorExtractionResult` 作为 `Profile Editor` 的正式抽取中间层对象。
- 顶层字段建议如下：

| 字段 | 含义 | 类型 | 必填 | 来源 | 说明 |
| --- | --- | --- | --- | --- | --- |
| `objectKey` | 固定对象键 | string | 是 | 固定值 | 固定为 `profileEditor` |
| `sourceRunId` | 来源 run | string | 是 | run 目录 | 用于回溯到正式 run |
| `sourceTaskType` | 来源任务类型 | string | 是 | `meta.json` / `results.json` | 当前预期为 `sine` |
| `sourceScreenshotPath` | 主来源拼接图 | string | 是 | `final_compare` | 当前主口径为 `testlab_table_profileeditortablescan_stitched.png` |
| `evidenceCollection` | 证据集合 | array | 是 | run 目录 | 列出拼接图、原图、必要裁剪图 |
| `rows` | 行级抽取结果 | array | 是 | 表格扫描结果 | 行顺序必须与截图中的自上而下顺序一致 |
| `mappingStatus` | 字段映射状态 | string | 是 | 抽取阶段赋值 | 允许值：`unmapped` / `partially_mapped` / `mapped` |
| `reviewStatus` | 复核状态 | string | 是 | 抽取阶段赋值 | 允许值：`pending_review` / `reviewed` |
| `notes` | 备注 | string | 否 | 抽取阶段赋值 | 记录本轮特殊说明 |

### 14.2 行级对象

- `rows[]` 中每一项建议定义为 `ProfileEditorExtractedRow`。

| 字段 | 含义 | 类型 | 必填 | 来源 | 说明 |
| --- | --- | --- | --- | --- | --- |
| `rowIndex` | 行序号 | integer | 是 | 行顺序 | 以最终拼接图中的视觉顺序为准，从 1 开始 |
| `rowKey` | 行主键 | string | 是 | 运行时生成 | 建议格式 `profile_editor_row_{rowIndex}` |
| `cells` | 单元格集合 | array | 是 | 行内抽取 | 至少保留原始列顺序 |
| `rowEvidenceRef` | 行级证据 | object | 是 | 裁剪/定位 | 必须能回到该行在拼接图中的位置 |
| `rowConfidence` | 行级置信度 | number | 否 | 抽取阶段赋值 | 0~1，允许为空 |
| `reviewStatus` | 行复核状态 | string | 是 | 抽取阶段赋值 | 允许值：`pending_review` / `reviewed` / `needs_attention` |
| `issues` | 行级问题列表 | array | 否 | 抽取阶段赋值 | 记录缺证据、低置信度、单位不明等问题 |

### 14.3 单元格对象

- `cells[]` 中每一项建议定义为 `ProfileEditorExtractedCell`。
- 当前前 9 列已使用正式 `fieldKey`；若后续新增列尚未确认，仍统一采用保守命名：
  - 已确认列使用正式 `fieldKey`
  - 未确认新增列统一使用 `col_10`、`col_11` 这类中性键

| 字段 | 含义 | 类型 | 必填 | 来源 | 说明 |
| --- | --- | --- | --- | --- | --- |
| `columnIndex` | 列序号 | integer | 是 | 列顺序 | 从左到右，从 1 开始 |
| `fieldKey` | 正式字段键 | string | 是 | 映射表/临时键 | 已确认列使用正式键；未确认新增列使用 `col_XX` |
| `fieldLabelRaw` | 原始列头/标签 | string | 否 | 视觉文本 | 若本轮无法稳定识别，可为空 |
| `rawText` | 原始文本 | string | 是 | OCR/人工抽取 | 必须保留原文，不可丢失 |
| `normalizedValue` | 归一化值 | string | 否 | 归一化结果 | 不允许覆盖 `rawText` |
| `rawUnit` | 原始单位 | string | 否 | OCR/人工抽取 | 仅在能从视觉证据中分离时写入 |
| `normalizedUnit` | 归一化单位 | string | 否 | 归一化结果 | 仅在单位映射已定义时写入 |
| `confidence` | 单元格置信度 | number | 否 | 抽取阶段赋值 | 0~1，允许为空 |
| `evidenceRef` | 单元格证据 | object | 是 | 裁剪/定位 | 无证据不得出正式值 |
| `normalizationNotes` | 归一化说明 | array | 否 | 归一化阶段赋值 | 记录裁剪、清洗、单位换写等动作 |
| `reviewStatus` | 单元格复核状态 | string | 是 | 抽取阶段赋值 | 允许值：`pending_review` / `reviewed` / `needs_attention` |

### 14.4 `evidenceRef` 契约

- `evidenceRef` 必须作为所有正式字段的硬门槛。
- 建议最小结构如下：

| 字段 | 含义 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- | --- |
| `screenshotPath` | 来源截图路径 | string | 是 | 优先指向 `final_compare` 拼接图 |
| `cropPath` | 裁剪图路径 | string | 否 | 若存在单元格/行裁剪图则写入 |
| `bbox` | 定位框 | object | 是 | 至少包含 `x / y / width / height` |
| `pageIndex` | 来源页序 | integer | 否 | 若能定位到 chunk 来源页，则写入 |
| `rowIndex` | 所属行号 | integer | 否 | 与 `rows[].rowIndex` 对齐 |
| `columnIndex` | 所属列号 | integer | 否 | 与 `cells[].columnIndex` 对齐 |
| `sourceType` | 证据类型 | string | 是 | 允许值建议：`stitched` / `chunk` / `crop` |

- 约束：
  - 无 `evidenceRef` 的字段不得进入正式结果
  - `bbox` 必须能让人工在截图中快速定位对应字段
  - `cropPath` 缺失不一定失败，但 `screenshotPath + bbox` 不得同时缺失

### 14.5 当前真实样图基线

- 当前 V2 字段契约已绑定 1 组真实成功样图，不再停留在纯抽象定义。
- 推荐样图基线 run：
  - `01bc27ae8d7a44878bdfd372da7ed334`
- 当前已确认的正式样图/证据路径：
  - 最终核验拼接图：
    - `artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png`
  - 原始拼接图：
    - `artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/evidence/testlab_table_profileeditortablescan_stitched.png`
  - 三张唯一 chunk：
    - `artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/evidence/testlab_table_profileeditortablescan_v_00.png`
    - `artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/evidence/testlab_table_profileeditortablescan_v_01.png`
    - `artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/evidence/testlab_table_profileeditortablescan_v_02.png`
  - 表格一致性报告：
    - `artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/table_evidence_profileeditortablescan.json`
  - 翻页事件报告：
    - `artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/table_scroll_events_profileeditortablescan.json`
- 当前已确认的样图事实：
  - `ChunkCount = 3`
  - `UniqueChunkCount = 3`
  - `ChangedEventCount = 2`
  - `IsConsistent = true`
  - 去重主口径当前使用 `DedupKey = SerialSha256`
- 当前样图可支持的最小结论：
  - 已存在可复用的真实拼接图，不需要再为 V2 契约补跑新样本
  - 左侧 `SerialRoi` 已被正式运行用于去重和变化检测，可作为后续 `col_01` 候选列的优先观察对象
  - 除 `SerialRoi` 外，其他业务列仍需基于真实样图做人工映射，当前不得凭空命名

### 14.6 第一版正式字段映射表

- 当前已基于用户填写的 Excel 样板，完成第一轮人工确认。
- 这批字段已可作为 `Profile Editor` V2 的第一版正式 `fieldKey` 映射。
- 当前统一命名规则：
  - 英文原列头保留在 `人工确认列头`
  - 正式键统一使用小写 `snake_case`
  - 列头中自带的单位不写入 `fieldKey`，单位信息单独保留

| 临时键 | 人工确认列头 | 建议正式 `fieldKey` | 是否带单位 | 是否枚举/状态列 | 是否关键比对字段 | 当前状态 | 备注 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `col_01` | `Serial Number` | `serial_number` | 否 | 否 | 是 | 已确认 | 当前既作为左侧定位/去重主列，也进入关键比对字段集合 |
| `col_02` | `Frequency (Hz)` | `frequency` | 是 | 否 | 是 | 已确认 | 列头已定义默认单位 `Hz` |
| `col_03` | `Acceleration (g)` | `acceleration` | 是 | 否 | 是 | 已确认 | 列头已定义默认单位 `g` |
| `col_04` | `Velocity (m/s)` | `velocity` | 是 | 否 | 否 | 已确认 | 列头已定义默认单位 `m/s` |
| `col_05` | `Displacement (mm)` | `displacement` | 是 | 否 | 是 | 已确认 | 列头已定义默认单位 `mm` |
| `col_06` | `Upper Alarm (dB)` | `upper_alarm` | 是 | 否 | 是 | 已确认 | 列头已定义默认单位 `dB` |
| `col_07` | `Lower Alarm (dB)` | `lower_alarm` | 是 | 否 | 是 | 已确认 | 列头已定义默认单位 `dB` |
| `col_08` | `Upper Abort (dB)` | `upper_abort` | 是 | 否 | 否 | 已确认 | 列头已定义默认单位 `dB` |
| `col_09` | `Lower Abort (dB)` | `lower_abort` | 是 | 否 | 否 | 已确认 | 列头已定义默认单位 `dB` |

- 当前映射表的使用规则：
  - 已确认列后续不再回退成 `col_XX` 占位键
  - 若后续新增更多列，仍先以 `col_10`、`col_11` 这类临时键进入人工确认流程
  - 若某列未来被证伪或语义变更，先更新文档映射，再更新实现代码

### 14.7 第一版抽取样例

- 以下样例已对齐当前第一版正式 `fieldKey` 映射。
- 以下样例仍然只演示正式结构，不代表真实业务值，也不代表当前已经具备真实 `bbox/crop` 抽取结果。
- 为了便于后续实现直接复用，当前同结构样板已独立落盘到：
  - `docs/15-Profile Editor字段契约样例.json`

```json
{
  "objectKey": "profileEditor",
  "sourceRunId": "01bc27ae8d7a44878bdfd372da7ed334",
  "sourceTaskType": "sine",
  "sourceScreenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
  "mappingStatus": "mapped",
  "reviewStatus": "pending_review",
  "rows": [
    {
      "rowIndex": 1,
      "rowKey": "profile_editor_row_1",
      "rowEvidenceRef": {
        "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
        "bbox": {
          "x": 0,
          "y": 0,
          "width": 0,
          "height": 0
        },
        "rowIndex": 1,
        "sourceType": "stitched"
      },
      "cells": [
        {
          "columnIndex": 1,
          "fieldKey": "serial_number",
          "fieldLabelRaw": "Serial Number",
          "rawText": "<raw_text_from_crop>",
          "normalizedValue": "<normalized_value>",
          "rawUnit": null,
          "normalizedUnit": null,
          "reviewStatus": "pending_review",
          "evidenceRef": {
            "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
            "bbox": {
              "x": 0,
              "y": 0,
              "width": 0,
              "height": 0
            },
            "rowIndex": 1,
            "columnIndex": 1,
            "sourceType": "stitched"
          },
          "normalizationNotes": [
            "fieldKey mapped from first confirmed column",
            "serial number has no explicit unit"
          ]
        },
        {
          "columnIndex": 2,
          "fieldKey": "frequency",
          "fieldLabelRaw": "Frequency (Hz)",
          "rawText": "<raw_text_from_crop>",
          "normalizedValue": "<normalized_value>",
          "rawUnit": "Hz",
          "normalizedUnit": "Hz",
          "reviewStatus": "pending_review",
          "evidenceRef": {
            "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
            "bbox": {
              "x": 0,
              "y": 0,
              "width": 0,
              "height": 0
            },
            "rowIndex": 1,
            "columnIndex": 2,
            "sourceType": "stitched"
          },
          "normalizationNotes": [
            "unit defined by column header",
            "fieldKey excludes unit suffix"
          ]
        },
        {
          "columnIndex": 3,
          "fieldKey": "acceleration",
          "fieldLabelRaw": "Acceleration (g)",
          "rawText": "<raw_text_from_crop>",
          "normalizedValue": "<normalized_value>",
          "rawUnit": "g",
          "normalizedUnit": "g",
          "reviewStatus": "pending_review",
          "evidenceRef": {
            "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
            "bbox": {
              "x": 0,
              "y": 0,
              "width": 0,
              "height": 0
            },
            "rowIndex": 1,
            "columnIndex": 3,
            "sourceType": "stitched"
          },
          "normalizationNotes": [
            "unit defined by column header",
            "placeholder only until real crop coordinates are confirmed"
          ]
        },
        {
          "columnIndex": 4,
          "fieldKey": "velocity",
          "fieldLabelRaw": "Velocity (m/s)",
          "rawText": "<raw_text_from_crop>",
          "normalizedValue": "<normalized_value>",
          "rawUnit": "m/s",
          "normalizedUnit": "m/s",
          "reviewStatus": "pending_review",
          "evidenceRef": {
            "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
            "bbox": {
              "x": 0,
              "y": 0,
              "width": 0,
              "height": 0
            },
            "rowIndex": 1,
            "columnIndex": 4,
            "sourceType": "stitched"
          },
          "normalizationNotes": [
            "unit defined by column header",
            "currently not in key comparison set"
          ]
        },
        {
          "columnIndex": 5,
          "fieldKey": "displacement",
          "fieldLabelRaw": "Displacement (mm)",
          "rawText": "<raw_text_from_crop>",
          "normalizedValue": "<normalized_value>",
          "rawUnit": "mm",
          "normalizedUnit": "mm",
          "reviewStatus": "pending_review",
          "evidenceRef": {
            "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
            "bbox": {
              "x": 0,
              "y": 0,
              "width": 0,
              "height": 0
            },
            "rowIndex": 1,
            "columnIndex": 5,
            "sourceType": "stitched"
          },
          "normalizationNotes": [
            "unit defined by column header",
            "included in key comparison set"
          ]
        },
        {
          "columnIndex": 6,
          "fieldKey": "upper_alarm",
          "fieldLabelRaw": "Upper Alarm (dB)",
          "rawText": "<raw_text_from_crop>",
          "normalizedValue": "<normalized_value>",
          "rawUnit": "dB",
          "normalizedUnit": "dB",
          "reviewStatus": "pending_review",
          "evidenceRef": {
            "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
            "bbox": {
              "x": 0,
              "y": 0,
              "width": 0,
              "height": 0
            },
            "rowIndex": 1,
            "columnIndex": 6,
            "sourceType": "stitched"
          },
          "normalizationNotes": [
            "threshold field",
            "included in key comparison set"
          ]
        },
        {
          "columnIndex": 7,
          "fieldKey": "lower_alarm",
          "fieldLabelRaw": "Lower Alarm (dB)",
          "rawText": "<raw_text_from_crop>",
          "normalizedValue": "<normalized_value>",
          "rawUnit": "dB",
          "normalizedUnit": "dB",
          "reviewStatus": "pending_review",
          "evidenceRef": {
            "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
            "bbox": {
              "x": 0,
              "y": 0,
              "width": 0,
              "height": 0
            },
            "rowIndex": 1,
            "columnIndex": 7,
            "sourceType": "stitched"
          },
          "normalizationNotes": [
            "threshold field",
            "included in key comparison set"
          ]
        },
        {
          "columnIndex": 8,
          "fieldKey": "upper_abort",
          "fieldLabelRaw": "Upper Abort (dB)",
          "rawText": "<raw_text_from_crop>",
          "normalizedValue": "<normalized_value>",
          "rawUnit": "dB",
          "normalizedUnit": "dB",
          "reviewStatus": "pending_review",
          "evidenceRef": {
            "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
            "bbox": {
              "x": 0,
              "y": 0,
              "width": 0,
              "height": 0
            },
            "rowIndex": 1,
            "columnIndex": 8,
            "sourceType": "stitched"
          },
          "normalizationNotes": [
            "threshold field",
            "currently not in key comparison set"
          ]
        },
        {
          "columnIndex": 9,
          "fieldKey": "lower_abort",
          "fieldLabelRaw": "Lower Abort (dB)",
          "rawText": "<raw_text_from_crop>",
          "normalizedValue": "<normalized_value>",
          "rawUnit": "dB",
          "normalizedUnit": "dB",
          "reviewStatus": "pending_review",
          "evidenceRef": {
            "screenshotPath": "artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png",
            "bbox": {
              "x": 0,
              "y": 0,
              "width": 0,
              "height": 0
            },
            "rowIndex": 1,
            "columnIndex": 9,
            "sourceType": "stitched"
          },
          "normalizationNotes": [
            "threshold field",
            "currently not in key comparison set"
          ]
        }
      ]
    }
  ]
}
```

- 说明：
  - 当前 `bbox` 仍为占位值，必须等真实裁剪定位方案落定后才能替换成正式数值
  - 当前 `rawText` / `normalizedValue` 也只作为结构占位，不得拿它们充当真实样图内容
  - 这份样例的意义，是把“真实 run -> 正式 `fieldKey` -> 行 -> 单元格 -> evidenceRef”链路结构钉死
  - 对于 `Frequency (Hz)`、`Acceleration (g)` 这类列头自带单位的字段：
    - `fieldLabelRaw` 保留列头原文
    - `fieldKey` 只保留业务含义，不把单位写进键名
    - `rawUnit / normalizedUnit` 可直接继承列头定义的默认单位
  - 对于 `Upper Alarm (dB)`、`Lower Alarm (dB)`、`Upper Abort (dB)`、`Lower Abort (dB)` 这类阈值字段：
    - 当前也沿用“列头保留原文、fieldKey 不带单位”的同一规则
    - 是否进入关键比对字段集合，单独以后续映射表为准，不由样例本身推断
  - `docs/15-Profile Editor字段契约样例.json` 与本段 JSON 样例保持同一套结构口径；后续若发生字段结构变化，优先同步更新该独立样板文件，再回写本文档
  - 当前代码侧已新增最小 C# DTO/record 契约：
    - `CheckMind.App/Core/ProfileEditorExtractionContracts.cs`
    - 当前还已补齐最小 `store/load`：
      - `CheckMind.App/Core/ProfileEditorExtractionStore.cs`
      - 默认输出文件名：
        - `profile_editor_extraction.template.json`
      - 当后续接入 `RunContext` 时，当前默认落盘路径约定为：
        - `<runDirectory>/profile_editor_extraction.template.json`
      - 当前仓库内已提供正式导出脚本：
        - `scripts/export_profile_editor_template.ps1`
      - 当前脚本已支持以下参数：
        - `-TemplatePath`
        - `-RunsRoot`
        - `-RunId`
      - 默认用法：
        - `powershell -ExecutionPolicy Bypass -File .\scripts\export_profile_editor_template.ps1`
      - 指定样板源文件：
        - `powershell -ExecutionPolicy Bypass -File .\scripts\export_profile_editor_template.ps1 -TemplatePath .\docs\15-Profile Editor字段契约样例.json`
      - 固定 run 导出：
        - `powershell -ExecutionPolicy Bypass -File .\scripts\export_profile_editor_template.ps1 -TemplatePath .\docs\15-Profile Editor字段契约样例.json -RunId profile-editor-template-fixed-001`
    - 已用 `docs/15-Profile Editor字段契约样例.json` 做过一次最小 round-trip 探针验证，确认当前结构可被代码稳定反序列化并再序列化
    - 已额外确认默认落盘文件中的 `bbox` 键名保持为 `x / y / width / height`，与 `docs/15` 的 JSON 样板一致
    - 当前默认导出基线：
      - `runId = d78cb971c3ec4ec3bbacd6756d42df0e`
      - 导出文件：
        - `artifacts/probe-runs/d78cb971c3ec4ec3bbacd6756d42df0e/profile_editor_extraction.template.json`
      - 已确认：
        - `objectKey = profileEditor`
        - `mappingStatus = mapped`
        - `rowCount = 1`
        - `cellCount = 9`
    - 当前固定 `RunId` 导出基线：
      - `requestedRunId = profile-editor-template-fixed-001`
      - 导出文件：
        - `artifacts/probe-runs/profile-editor-template-fixed-001/profile_editor_extraction.template.json`
      - 已确认：
        - 终端输出 `requested_run_id=profile-editor-template-fixed-001`
        - 终端输出 `run_id=profile-editor-template-fixed-001`
        - `default_file_name = profile_editor_extraction.template.json`
        - `objectKey = profileEditor`
        - `mappingStatus = mapped`
        - `rowCount = 1`
        - `cellCount = 9`
    - 本轮仍不包含真实 OCR、裁剪定位或 UI 接线

### 14.8 人工字段映射确认表

- 以下表格保留为后续新增列或修订列的继续确认模板。
- 填写原则：
  - 先按真实样图从左到右确认列顺序
  - 再确认列头/字段含义
  - 最后确认是否带单位、是否需要枚举字典、是否属于关键比对字段
- 对于尚未确认的新增列，`fieldKey` 不得从 `col_XX` 擅自升级为真实业务字段键。

| 临时键 | 列序号 | 样图观察位置 | 当前候选理解 | 人工确认列头 | 建议正式 `fieldKey` | 是否带单位 | 是否枚举/状态列 | 是否关键比对字段 | 当前状态 | 备注 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `col_10_plus` | 10+ | 拼接图后续列 | 后续列集合占位 | 待填写 | 待填写 | 待填写 | 待填写 | 待填写 | 待确认 | 当前前 9 列已完成首轮人工确认，后续新增列从 `col_10` 开始继续扩展 |

### 14.9 人工确认操作说明

1. 打开当前基线样图：
   - `artifacts/probe-runs/01bc27ae8d7a44878bdfd372da7ed334/screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png`
2. 以左到右顺序对照上表，先确认每一列在视觉上的位置，不急于命名。
3. 若能从样图、模板或业务侧说明确认列头，则填写：
   - `人工确认列头`
   - `建议正式 fieldKey`
4. 若某列存在单位，则额外标记：
   - `是否带单位 = 是`
   - 并在备注中说明单位样式是否独立显示
5. 若某列本质是状态/枚举，则额外标记：
   - `是否枚举/状态列 = 是`
   - 并在备注中记录候选枚举值样式
6. 若某列属于后续规则比对的核心字段，则标记：
   - `是否关键比对字段 = 是`
7. 若某列当前仍无法确认，则保持：
   - `当前状态 = 待确认`
   - 不要为了推进速度强行命名

### 14.10 人工确认完成后的收口规则

- 只有同时满足以下条件，才允许把 `col_XX` 收口为正式 `fieldKey`：
  - 有真实样图证据
  - 有人工确认列头或模板侧明确映射
  - 能说明该字段是否带单位/是否需要枚举字典
- 若只确认了列位置，但没有确认字段语义，则：
  - 保留 `col_XX`
  - `mappingStatus = partially_mapped`
- 若已确认字段语义，但还没有 `bbox/crop` 方案，则：
  - 可以先更新映射表
  - 但不得声称该字段已经具备正式抽取结果

## 15. V2 归一化规则

### 15.1 总原则

- 归一化只做“保守、可回退、可追溯”的转换。
- `rawText` 永远保留，`normalizedValue` 只做派生，不允许覆盖原文。
- 未确认规则前，宁可保留原值并标记 `pending_review`，也不做猜测性改写。

### 15.2 当前允许的保守归一化

- 去掉首尾空白
- 合并重复空格
- 统一换行为空格或结构化分段
- 保留数字、符号、单位原样，不在无规则时擅自换算
- 全角/半角仅在不改变业务含义时才可统一

### 15.3 当前禁止的高风险归一化

- 对未进入已确认映射表的视觉列，直接改写成业务字段名
- 在没有单位规则前，擅自把 `rawUnit` 换算成其他单位
- 在没有枚举字典前，把原始文本强制归入布尔值、等级或状态码
- 在证据不足时补写默认值

## 16. V2 验收口径

### 16.1 成功标准

- 文档层成功标准：
  - `Profile Editor` 已拥有正式的 V2 字段契约定义
  - 已明确 `rows / cells / evidenceRef / normalization / reviewStatus` 的最小结构
  - 已明确“无样本时不得编造业务字段名”的边界
  - 已确认前 9 列的第一版正式 `fieldKey` 映射与关键比对字段集合
- 实现前置成功标准：
  - 已准备 1 份真实 `Profile Editor` 表格样图
  - 已准备 1 版正式字段映射表
  - 已确认 1 组“字段抽取 -> 归一化 -> evidenceRef”样例

### 16.2 失败/阻塞信号

- 若没有真实样图，则不得进入真实字段命名阶段
- 若新增列没有字段映射表，则该列 `fieldKey` 只能维持 `col_XX` 中间层键
- 若字段无法绑定 `evidenceRef`，则该字段不能进入正式结果
- 若归一化规则会改变业务语义且无法说明依据，则必须回退为 `rawText + pending_review`

## 17. V2 实施顺序建议

1. 已基于当前文档固化中间层字段契约，并补齐最小代码样板与导出入口
2. 已绑定 1 份真实 `Profile Editor` 样图，并完成前 9 列第一版正式字段映射
3. 后续若出现新增列，继续按 `col_XX -> 人工确认 -> 正式 fieldKey` 的顺序扩展
4. 在现有契约基础上再决定：
   - 是先做规则型抽取
   - 还是先做 OCR/AI 辅助抽取
5. 最后再把抽取结果接入比对/告警链路

## 18. 当前开放问题

- `Profile Editor` 当前真实业务列头与字段含义是什么
- 是否存在单位列、枚举列、范围列等需要单独归一化的列
- 业务侧最终希望比对的是“逐行全字段”，还是“少量关键字段”
- 真实字段映射是否由模板侧提供，还是需要我们从样图先逆向整理
