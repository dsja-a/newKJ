"""直接测试飞书 WebSocket 连接 - 独立于 Keji 服务运行

用法: python scripts/test_feishu_ws.py
"""
import os
import sys
import time
import json
import asyncio
import threading

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Fix Windows console encoding
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

def main():
    from core.security.secrets import load_dotenv_file, load_app_config
    from pathlib import Path

    root = Path(__file__).resolve().parent.parent
    load_dotenv_file(root)
    config = load_app_config()

    feishu = config.get("channels", {}).get("feishu", {})
    app_id = feishu.get("app_id", "")
    app_secret = feishu.get("app_secret", "")
    domain = feishu.get("domain", "feishu")

    print("=" * 60)
    print("  飞书 WebSocket 连接测试")
    print("=" * 60)
    print(f"\nApp ID: {app_id[:10]}***")
    print(f"App Secret: {'***已设置***' if app_secret else '未设置！'}")
    print(f"Domain: {domain}")

    if not app_id or not app_secret:
        print("\n[错误] 缺少 App ID 或 App Secret，请检查 .env 文件")
        return

    # 清理代理
    for var in ("ALL_PROXY", "all_proxy", "SOCKS_PROXY", "socks_proxy"):
        if os.environ.pop(var, None):
            print(f"已移除代理变量: {var}")

    import lark_oapi as lark
    from lark_oapi.api.im.v1 import P2ImMessageReceiveV1

    lark_domain = lark.FEISHU_DOMAIN if domain == "feishu" else lark.LARK_DOMAIN

    print(f"\n1. 创建 Lark Client (domain={lark_domain})...")

    client = (
        lark.Client.builder()
        .app_id(app_id)
        .app_secret(app_secret)
        .domain(lark_domain)
        .log_level(lark.LogLevel.DEBUG)  # 详细日志
        .build()
    )

    # 先测试 REST API 能否获取 tenant_access_token
    print("\n2. 测试 REST API (获取 bot info)...")
    try:
        request = (
            lark.BaseRequest.builder()
            .http_method(lark.HttpMethod.GET)
            .uri("/open-apis/bot/v3/info")
            .token_types({lark.AccessTokenType.APP})
            .build()
        )
        response = client.request(request)
        if response.success():
            data = json.loads(response.raw.content)
            bot_name = (data.get("data", {}) or {}).get("bot", {}).get("name", "?")
            print(f"   REST API 成功! 机器人名称: {bot_name}")
        else:
            print(f"   REST API 失败! code={response.code}, msg={response.msg}")
            print(f"   请检查 App ID 和 App Secret 是否正确")
            return
    except Exception as e:
        print(f"   REST API 异常: {e}")
        return

    # 测试 WebSocket 长连接
    print("\n3. 测试 WebSocket 长连接...")
    print("   (会在 10 秒后自动断开)")

    ws_connected = threading.Event()
    ws_error = threading.Event()
    ws_received_msg = threading.Event()

    def on_message(data: P2ImMessageReceiveV1):
        ws_received_msg.set()
        event = data.event
        msg = event.message
        print(f"\n   >>> 收到消息! type={msg.message_type}, "
              f"chat_type={msg.chat_type}, content={msg.content[:80]}")

    # 构建事件处理器
    builder = lark.EventDispatcherHandler.builder("", "")
    builder.register_p2_im_message_receive_v1(on_message)
    handler = builder.build()

    # 创建 WebSocket 客户端
    ws_client = lark.ws.Client(
        app_id, app_secret,
        domain=lark_domain,
        event_handler=handler,
        log_level=lark.LogLevel.DEBUG,
    )

    def run_ws():
        import lark_oapi.ws.client as _lark_ws_client
        ws_loop = asyncio.new_event_loop()
        asyncio.set_event_loop(ws_loop)
        _lark_ws_client.loop = ws_loop
        try:
            print("   WebSocket 线程已启动，正在连接...")
            ws_connected.set()  # 表示开始尝试连接
            ws_client.start()
            # start() 在连接断开时返回
            print("   WebSocket 连接已断开")
        except Exception as e:
            ws_error.set()
            print(f"   WebSocket 错误: {e}")
        finally:
            ws_loop.close()

    ws_thread = threading.Thread(target=run_ws, daemon=True)
    ws_thread.start()

    # 等待连接
    ws_connected.wait(timeout=10)
    if not ws_connected.is_set():
        print("   [失败] WebSocket 线程未能启动")
        return

    # 等待一会儿看是否收到消息
    print("   WebSocket 已启动，等待消息中...")
    print("   请在飞书 App 给机器人发送一条消息...")
    time.sleep(15)

    if ws_received_msg.is_set():
        print("\n[成功!] WebSocket 连接正常，消息接收成功！")
    elif ws_error.is_set():
        print("\n[失败!] WebSocket 连接出错，请检查网络和飞书开放平台配置")
    else:
        print("\n[超时] 15 秒内未收到消息。可能原因：")
        print("   1. 未在飞书 App 中给机器人发消息")
        print("   2. 飞书开放平台 → 事件订阅 → 未订阅 im.message.receive_v1")
        print("   3. 飞书开放平台 → 机器人 → 未启用")
        print("   4. 飞书开放平台 → 版本管理与发布 → 未发布")

    print("\n提示：按 Ctrl+C 退出")

if __name__ == "__main__":
    main()
