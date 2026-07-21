# Profile Editor 任务级验收计划

## 1. 标准命令

### 1.1 主验收命令

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run_testlab_with_notch_profiles.ps1 -NotchProfileCount 3
```

用途：

- 复用当前已通过的 `sine` 任务级正式链路，验证 `Profile Editor` 在真实任务中仍能稳定进入、扫描、关闭并返回父窗口。
- 该命令同时会跑 `Channel Setup`、`Sine Setup`、`Profile Editor`、`Advanced Control Setup` 与 `Notch Profiles`，其中本计划只裁决 `Profile Editor` 相关证据。

### 1.2 基线对照 run

- run `8f91b14758d34f42ae6ddb8564d26cfd`
  - 用于对照 `Profile Editor` 翻页稳定化后的正式业务基线
- run `718dc8781c074e79ae8aed839ef19f7d`
  - 用于对照 `bootstrap PgDn -> PgUp 回顶 -> 正式两次 PgDn` 固定流程基线
- run `39fb7de04da24e01929548d864b58ec1`
  - 用于对照当前任务级端到端主口径成功基线

### 1.3 首轮执行结果

- 已完成首轮 `Profile Editor` 任务级验收：
  - run `d33468c64aad4cfbb4be0429371d28c3`
- 本轮结论：
  - `results.json` 记录 `status=completed`、`completedNotchProfileCount=3`、`requestedNotchProfileCount=3`、`alerts=[]`
  - `testlab_run.json` 记录 `OpenedWindowTitle="Profile Editor"`、`UniqueChunkCount=3`、`StitchedScreenshotPath` 已落盘、`ChildWindowClosed=true`、`ErrorMessage=null`
  - `testlab_phases.log` 记录了 `bootstrap PgDn -> PgUp 回顶 -> 正式两次 PgDn` 的关键阶段
- 首轮证据路径：
  - `artifacts/probe-runs/d33468c64aad4cfbb4be0429371d28c3/results.json`
  - `artifacts/probe-runs/d33468c64aad4cfbb4be0429371d28c3/testlab_run.json`
  - `artifacts/probe-runs/d33468c64aad4cfbb4be0429371d28c3/testlab_phases.log`
  - `artifacts/probe-runs/d33468c64aad4cfbb4be0429371d28c3/screenshots/evidence/testlab_tabs_01_profileeditor_window_opened.png`
  - `artifacts/probe-runs/d33468c64aad4cfbb4be0429371d28c3/screenshots/evidence/testlab_table_profileeditortablescan_stitched.png`
  - `artifacts/probe-runs/d33468c64aad4cfbb4be0429371d28c3/screenshots/evidence/testlab_tabs_01_profileeditor_returned_to_parent.png`

## 2. 证据清单

每次验收至少收以下 4 类证据，缺一项即不得宣称“已通过”。

### 2.1 终端证据

- 实际执行命令
- 终端末尾输出
- `last_run=...`
- 若发生异常退出，保留终端错误全文

### 2.2 结构化结果

- `results.json`
  - 用于确认主任务没有因 `Profile Editor` 扫描异常而静默失败
- `testlab_run.json`
  - 必须能定位到 `Profile Editor` 对应的子窗口结果
  - 至少确认以下字段：
    - `ProfileEditorFormalResult.ChunkCount`
    - `ProfileEditorFormalResult.UniqueChunkCount`
    - `ProfileEditorFormalResult.FinalCompareScreenshotPath`
    - `ProfileEditorFormalResult.ChildWindowClosed`
    - `ProfileEditorFormalResult.ReturnedToParent`
    - `ProfileEditorFormalResult.FlowVerified`
    - `FinalCompareArtifacts`
- `testlab_phases.log`
  - 必须能看到 `Profile Editor` 的进入、扫描、关闭返回等关键阶段

### 2.3 可视化证据

- `screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png`
- `screenshots/evidence/` 中原始 `stitched.png`
- 必要的 chunk/debug 截图
- 如失败，保留失败前最后一屏截图

### 2.4 关键行为证据

证据必须能回答以下问题：

- 是否真正进入 `Profile Editor` 子窗口
- 是否真正执行了 `bootstrap PgDn -> PgUp 回顶 -> 正式两次 PgDn`
- 是否真正形成 `Chunks=3 / UniqueChunkCount=3`
- 是否真正关闭子窗口并返回父窗口
- 是否未触发 `Testlab EXCEPTION_ACCESS_VIOLATION`

## 3. 通过判据

以下条件同时满足，才判本轮 `Profile Editor` 任务级验收通过：

- `results.json` 已正常落盘
- `testlab_run.json` 已正常落盘
- `testlab_phases.log` 已正常落盘
- `Profile Editor` 已在真实任务链路中被正常打开
- `Profile Editor` 已按当前固定口径执行：
  - `bootstrap PgDn`
  - `PgUp` 回顶
  - 正式两次 `PgDn`
- `testlab_run.json` 中确认：
  - `ProfileEditorFormalResult.ChunkCount = 3`
  - `ProfileEditorFormalResult.UniqueChunkCount = 3`
  - `ProfileEditorFormalResult.FinalCompareScreenshotPath` 非空且文件已生成
  - `ProfileEditorFormalResult.ChildWindowClosed = true`
  - `ProfileEditorFormalResult.ReturnedToParent = true`
  - `ProfileEditorFormalResult.FlowVerified = true`
- 子窗口关闭后，父窗口链路继续正常，不出现“扫完 `Profile Editor` 后主链路退化”
- 整轮运行中未出现 `Testlab EXCEPTION_ACCESS_VIOLATION`

## 4. 失败判据

出现以下任一情况，即判本轮 `Profile Editor` 任务级验收失败：

- 没有 `last_run`
- `results.json` 未落盘
- `testlab_run.json` 未落盘
- `testlab_phases.log` 未落盘
- 未能进入 `Profile Editor`
- 进入后点击落入文本编辑态，导致分页语义失效
- 未执行 `bootstrap PgDn` 就直接尝试 `PgUp`，导致伪回顶
- `ProfileEditorFormalResult.ChunkCount < 3`
- `ProfileEditorFormalResult.UniqueChunkCount < 3`
- 未产出 `screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png`
- `ProfileEditorFormalResult.ChildWindowClosed != true`
- `ProfileEditorFormalResult.ReturnedToParent != true`
- `ProfileEditorFormalResult.FlowVerified != true`
- 扫描完成后未返回父窗口，或返回后主链路异常
- 出现 `Testlab EXCEPTION_ACCESS_VIOLATION`

## 5. 执行顺序

### 5.1 单轮执行顺序

1. 运行主验收命令
2. 记录终端末尾 `last_run=...`
3. 读取对应 run 目录中的：
   - `results.json`
   - `testlab_run.json`
   - `testlab_phases.log`
4. 核对 `screenshots/final_compare/testlab_table_profileeditortablescan_stitched.png` 是否已生成
5. 按本计划第 3 节 / 第 4 节判定通过或失败

### 5.2 失败后最小输出

若失败，必须按固定格式记录：

- 实际结果
- 证据路径
- 当前结论
- 下一步最小动作

### 5.3 当前建议

- 先执行 1 轮任务级验收，不同时并发别的对象专项调试。
- 若本轮通过，再决定是否进入下一步：
  - 收口 `testlab_run.json` 中 `Profile Editor` 的正式结果字段
  - 或继续规划 `Profile Editor` 的下一阶段结构化输出
