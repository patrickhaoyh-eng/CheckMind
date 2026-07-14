# Output Template

本模板用于配合 `build-windows-offline-desktop-package` Skill 使用。  
目标是把每次离线桌面打包的最终交付结果用统一格式表达出来，避免只给出一个 `exe` 路径而缺少输入来源、版本、校验和限制说明。

## 标准交付模板

```markdown
# Windows Offline Desktop Package Delivery

## Input Source
- Product name:
- Source repository:
- Source branch:
- Source commit / tag:
- Packaging workspace:
- Build date:

## Version
- Product version:
- Installer version:
- Patch version:

## Outputs
- Full installer:
- Full installer alias:
- Full installer sha256:
- Additional patch package:
- Additional patch sha256:

## Runtime Staging
- Python runtime:
- WebView2 strategy:
- Desktop shell:
- Launcher scripts:

## Packaging Scope
- Program layer included:
- Data layer preserved:
- Default install root:
- Upgrade detection strategy:

## Validation Done
- Assemble completed:
- Frontend build completed:
- Desktop publish completed:
- Installer build completed:
- First install verified:
- Restart verified:
- Upgrade overwrite verified:

## Known Limits
- 
- 

## Recommended Acceptance
- Installed machine -> patch upgrade
- Installed machine -> full overwrite upgrade
- Clean machine -> fresh full install

## Notes
- 
```

## 建议填写规则

### Input Source
- 必须明确源码来源，避免以后只知道包名，不知道是基于哪次源码构建的
- 如果 `_DEV` 里存在打包侧热修，也应显式写出

### Version
- 同时记录产品版本与安装器版本
- 如果存在别名命名方案，也应写清楚

### Outputs
- 不要只给主 `exe`
- 必须一起给出：
  - 别名包
  - `sha256`
  - 若本次有附带补丁包，也一并列出

### Runtime Staging
- 必须说明运行时是“内置”还是“前置条件”
- 对 `.NET Desktop Runtime`、WebView2 这类依赖，不要让读者猜

### Validation Done
- 验证要写功能结果，不只写“build success”
- 如果某项未做，也要明确标记未做

## 最小可接受填写示例

```markdown
## Input Source
- Product name: YinheAIAssistant
- Source repository: ReAIMonitor
- Source branch: intranet-deploy
- Source commit / tag: 73b9754
- Packaging workspace: D:\CodeProject\Monitor\YinheAIAssistant_DEV
- Build date: 2026-07-01

## Version
- Product version: 0.1.1
- Installer version: 1.0.1
- Patch version: 0.1.1

## Outputs
- Full installer: D:\CodeProject\Monitor\YinheAIAssistant_DEV\dist\YinheAIAssistant-setup-0.1.1.exe
- Full installer alias: D:\CodeProject\Monitor\YinheAIAssistant_DEV\dist\YinheAIAssistant-Setup-1.0.1-dev-x64.exe
- Full installer sha256: D:\CodeProject\Monitor\YinheAIAssistant_DEV\dist\YinheAIAssistant-Setup-1.0.1-dev-x64.sha256.txt
- Additional patch package: D:\CodeProject\Monitor\YinheAIAssistant_DEV\dist\YinheAIAssistant-frontend-update-0.1.1.exe
- Additional patch sha256: D:\CodeProject\Monitor\YinheAIAssistant_DEV\dist\YinheAIAssistant-frontend-update-0.1.1.sha256.txt
```

## 不合格示例

以下输出不合格：

```markdown
包已经打好了，在 dist 目录里。
```

原因：
- 没有版本号
- 没有源码来源
- 没有校验文件
- 没有说明依赖策略
- 没有说明哪些验收已完成

## 完成定义

只有当执行者能够按本模板给出完整交付说明时，才说明本次离线桌面打包真正完成。
