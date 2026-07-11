"""监听飞书 WebSocket 的所有事件，打印每个事件的类型和内容"""
import os, sys, json, time, asyncio, threading

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pathlib import Path
from core.security.secrets import load_dotenv_file, load_app_config

root = Path(__file__).resolve().parent.parent
load_dotenv_file(root)
cfg = load_app_config()
f = cfg["channels"]["feishu"]

for v in ("ALL_PROXY", "all_proxy", "SOCKS_PROXY", "socks_proxy"):
    os.environ.pop(v, None)
os.environ["NO_PROXY"] = "*"

import lark_oapi as lark

app_id = f["app_id"]
app_secret = f["app_secret"]
domain = lark.FEISHU_DOMAIN

print("=" * 60)
print("  飞书 WebSocket 全事件监听器")
print(f"  App: {app_id[:10]}***")
print("  监听所有事件类型，打印任何收到的事件")
print("=" * 60)

received_any = threading.Event()

# 创建一个捕获所有事件的 handler
class CatchAllHandler:
    """捕获所有事件的处理器"""
    def __getattr__(self, name):
        if name.startswith("_"):
            raise AttributeError(name)
        def handler(data):
            received_any.set()
            event_type = type(data).__name__
            print(f"\n>>> 收到事件: {event_type}")
            # 尝试打印事件的关键字段
            if hasattr(data, 'event'):
                e = data.event
                if hasattr(e, 'message'):
                    m = e.message
                    print(f"    message_type: {getattr(m, 'message_type', '?')}")
                    print(f"    chat_type: {getattr(m, 'chat_type', '?')}")
                    print(f"    content: {str(getattr(m, 'content', ''))[:200]}")
                    print(f"    message_id: {getattr(m, 'message_id', '?')}")
                if hasattr(e, 'sender'):
                    s = e.sender
                    print(f"    sender_type: {getattr(s, 'sender_type', '?')}")
            sys.stdout.flush()
        return handler

# 构建事件处理器 - 手动注册各种常见事件
builder = lark.EventDispatcherHandler.builder("", "")
# 注册 im.message.receive_v1 (v2 协议)
builder.register_p2_im_message_receive_v1(CatchAllHandler())
# 也注册 v1 版本
try:
    builder.register_p1_im_message_receive_v1(CatchAllHandler())
except:
    pass

handler = builder.build()

ws_client = lark.ws.Client(
    app_id, app_secret,
    domain=domain,
    event_handler=handler,
    log_level=lark.LogLevel.DEBUG,
)

def run_ws():
    import lark_oapi.ws.client as _lark_ws_client
    ws_loop = asyncio.new_event_loop()
    asyncio.set_event_loop(ws_loop)
    _lark_ws_client.loop = ws_loop
    try:
        ws_client.start()
    except Exception as e:
        print(f"\nWebSocket 退出: {e}")
    finally:
        ws_loop.close()

ws_thread = threading.Thread(target=run_ws, daemon=True)
ws_thread.start()

print("\n监听中... 请在飞书给「科吉助手」发消息")
print("按 Ctrl+C 退出\n")

try:
    for i in range(30):
        time.sleep(2)
        if received_any.is_set():
            print("\n✅ 收到事件！")
            break
        print(".", end="", flush=True)
    else:
        print("\n\n❌ 60 秒内未收到任何事件")
        print("\n这意味着飞书服务器确实没有推送事件到 WebSocket")
        print("问题 100% 在飞书开放平台的事件订阅配置上")
except KeyboardInterrupt:
    pass
