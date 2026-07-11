"""检查飞书开放平台应用配置"""
import os, sys, json
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pathlib import Path
from core.security.secrets import load_dotenv_file, load_app_config

root = Path(__file__).resolve().parent.parent
load_dotenv_file(root)
cfg = load_app_config()
feishu = cfg["channels"]["feishu"]
app_id = feishu["app_id"]
app_secret = feishu["app_secret"]

for v in ("ALL_PROXY", "all_proxy", "SOCKS_PROXY", "socks_proxy"):
    os.environ.pop(v, None)

import lark_oapi as lark
client = (
    lark.Client.builder()
    .app_id(app_id)
    .app_secret(app_secret)
    .domain(lark.FEISHU_DOMAIN)
    .log_level(lark.LogLevel.ERROR)
    .build()
)

def api_get(path):
    req = lark.BaseRequest.builder() \
        .http_method(lark.HttpMethod.GET) \
        .uri(path) \
        .token_types({lark.AccessTokenType.APP}) \
        .build()
    resp = client.request(req)
    if resp.success():
        return json.loads(resp.raw.content)
    return {"_error": f"code={resp.code} msg={resp.msg}"}

# 1. Bot info
print("=" * 50)
print("1. 机器人信息")
print("-" * 30)
r = api_get("/open-apis/bot/v3/info")
if "_error" not in r:
    bot = (r.get("data") or r).get("bot") or {}
    print(f"   名称: {bot.get('app_name') or bot.get('name', '?')}")
    print(f"   open_id: {bot.get('open_id', '?')}")
    print(f"   激活状态: {bot.get('activate_status')}")
else:
    print(f"   {r['_error']}")

# 2. Event subscriptions
print()
print("2. 事件订阅")
print("-" * 30)
r = api_get("/open-apis/event/v1/app/event_subscription/list")
if "_error" not in r:
    events = (r.get("data") or {}).get("events", [])
    if events:
        for ev in events:
            print(f"   - {ev}")
    else:
        print("   [警告] 无任何事件订阅！需要添加 im.message.receive_v1")
        print("   -> 飞书开放平台 → 事件订阅 → 添加事件")
else:
    print(f"   {r['_error']}")

# 3. App visibility / publish status
print()
print("3. 应用发布状态")
print("-" * 30)
r = api_get(f"/open-apis/application/v6/applications/{app_id}/app_versions")
if "_error" not in r:
    versions = (r.get("data") or {}).get("items", [])
    if versions:
        for v in versions:
            status_map = {0: "未发布", 1: "审核中", 2: "已驳回", 3: "已发布"}
            status = status_map.get(v.get("status"), f"未知({v.get('status')})")
            print(f"   版本 {v.get('version','?')}: {status}")
    else:
        print("   [警告] 无版本记录 - 应用从未发布！")
        print("   -> 飞书开放平台 → 版本管理与发布 → 创建版本 → 申请发布")
else:
    print(f"   {r['_error']}")

# 4. Application basic info
print()
print("4. 应用基本信息")
print("-" * 30)
r = api_get(f"/open-apis/application/v6/applications/{app_id}")
if "_error" not in r:
    app_data = (r.get("data") or {}).get("app") or r.get("data") or {}
    print(f"   应用名: {app_data.get('name', '?')}")
    print(f"   描述: {app_data.get('description', '?')[:50]}")
    print(f"   状态: {app_data.get('status')} (0=正常)")
    print(f"   类型: {app_data.get('type')}")
else:
    print(f"   {r['_error']}")

print()
print("=" * 50)
print("如果上面有 [警告]，请在飞书开放平台修复后重试")
