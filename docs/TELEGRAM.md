# Telegram 推送（可选）

不使用推送时，无需创建任何配置。

配置位于 `%LOCALAPPDATA%\YuMir\AIHOT.Desktop\telegram.local.json`，包含 `bot_username`、`bot_token`、`channel_id`。可设置 `enabled: false` 暂停。机器人需具备向目标频道发送消息的权限。

推送跟随程序的数据刷新流程，首次运行建立基线，不批量补发历史条目。已发送标识与待发送队列保存在本机；明确失败会重试，结果不确定的中断请求不会盲目重发。

状态文件位于同目录的 `telegram-status.txt`。建议仅在一台电脑启用同一频道的推送，避免多机重复。

不要把真实 token、频道配置、推送状态或用户数据放进仓库、截图或 Issue。凭据泄露后应在服务提供方撤销并更换。
