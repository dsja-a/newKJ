"""飞书对接诊断脚本 - 独立测试飞书连接和 LLM 后端可用性

用法: python scripts/diagnose_feishu.py
"""

import os
import sys
import asyncio

# Fix Windows console encoding for emoji support
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# 将项目根目录加入 sys.path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def check_env() -> dict:
    """检查 .env 环境变量"""
    from core.security.secrets import load_dotenv_file, resolve_env_ref
    from pathlib import Path

    root = Path(__file__).resolve().parent.parent
    load_dotenv_file(root)

    results = {}
    for var in ["FEISHU_APP_ID", "FEISHU_APP_SECRET", "DEEPSEEK_API_KEY", "KEJI_ADMIN_PASSWORD"]:
        val = os.environ.get(var, "")
        masked = val[:8] + "***" if len(val) > 8 else ("(空)" if not val else val[:4] + "***")
        results[var] = {"set": bool(val), "value": masked}
    return results


def check_config() -> dict:
    """检查 config.yaml 飞书配置"""
    from core.security.secrets import load_app_config

    config = load_app_config()
    feishu = config.get("channels", {}).get("feishu", {})
    models = config.get("models", {})

    return {
        "feishu_enabled": feishu.get("enabled", False),
        "app_id": (feishu.get("app_id", "") or "")[:8] + "***" if feishu.get("app_id") else "(空)",
        "app_secret_set": bool(feishu.get("app_secret", "")),
        "domain": feishu.get("domain", "feishu"),
        "allow_from": feishu.get("allow_from", []),
        "streaming": feishu.get("streaming", True),
        "default_model": models.get("default", "unknown"),
        "model_config": {
            k: {
                "base_url": v.get("base_url", ""),
                "model": v.get("model", ""),
                "api_key_set": bool(v.get("api_key", "")),
            }
            for k, v in models.items()
            if k != "default" and isinstance(v, dict)
        },
    }


async def check_llm_backend(config: dict) -> dict:
    """检查 LLM 后端连通性"""
    from nanobot.providers.openai_compat_provider import OpenAICompatProvider

    models_cfg = config.get("models", {})
    default = models_cfg.get("default", "ollama")
    provider_cfg = models_cfg.get(default, {})

    base_url = (provider_cfg.get("base_url") or "").rstrip("/")
    api_key = provider_cfg.get("api_key", "")
    model = provider_cfg.get("model", "")

    # 解析环境变量引用
    if isinstance(api_key, str) and api_key.startswith("${") and api_key.endswith("}"):
        api_key = os.environ.get(api_key[2:-1], "")

    result = {
        "provider": default,
        "base_url": base_url,
        "model": model,
        "api_key_configured": bool(api_key),
        "reachable": False,
        "error": None,
    }

    try:
        provider = OpenAICompatProvider(
            api_key=api_key or "test",
            api_base=base_url,
            default_model=model,
        )
        # 简单的模型列表请求来测试连通性
        resp = await provider.chat(
            messages=[{"role": "user", "content": "hi"}],
            model=model,
        )
        result["reachable"] = True
        result["response_preview"] = (resp.content or "")[:100]
    except Exception as e:
        result["error"] = str(e)[:300]

    return result


async def check_feishu_sdk() -> dict:
    """检查飞书 SDK 可用性"""
    result = {"installed": False, "version": None, "error": None}

    try:
        import importlib.util
        spec = importlib.util.find_spec("lark_oapi")
        if spec:
            import lark_oapi
            result["installed"] = True
            result["version"] = getattr(lark_oapi, "__version__", "unknown")
        else:
            result["error"] = "lark-oapi 未安装，请执行: pip install lark-oapi"
    except Exception as e:
        result["error"] = str(e)

    return result


async def check_feishu_connection(config: dict) -> dict:
    """检查飞书 API 连通性（使用 REST API 获取机器人信息）"""
    import lark_oapi as lark

    feishu = config.get("channels", {}).get("feishu", {})
    app_id = feishu.get("app_id", "")
    app_secret = feishu.get("app_secret", "")
    domain = lark.FEISHU_DOMAIN if feishu.get("domain", "feishu") == "feishu" else lark.LARK_DOMAIN

    result = {
        "app_id_valid": bool(app_id),
        "api_reachable": False,
        "bot_info": None,
        "error": None,
    }

    if not app_id or not app_secret:
        result["error"] = "缺少 app_id 或 app_secret，请在 .env 中配置 FEISHU_APP_ID 和 FEISHU_APP_SECRET"
        return result

    try:
        client = (
            lark.Client.builder()
            .app_id(app_id)
            .app_secret(app_secret)
            .domain(domain)
            .log_level(lark.LogLevel.ERROR)
            .build()
        )

        # 获取机器人信息
        request = (
            lark.BaseRequest.builder()
            .http_method(lark.HttpMethod.GET)
            .uri("/open-apis/bot/v3/info")
            .token_types({lark.AccessTokenType.APP})
            .build()
        )
        response = client.request(request)

        if response.success():
            import json
            data = json.loads(response.raw.content)
            bot = (data.get("data") or data).get("bot") or {}
            result["api_reachable"] = True
            result["bot_info"] = {
                "app_name": bot.get("name", "unknown"),
                "open_id": bot.get("open_id", "")[:20] + "..." if bot.get("open_id") else "N/A",
            }
        else:
            result["error"] = f"API 返回错误: code={response.code}, msg={response.msg}"
    except Exception as e:
        result["error"] = f"连接失败: {str(e)[:300]}"

    return result


def check_websocket_access() -> dict:
    """检查 WebSocket 端口可达性"""
    import socket

    host = "open.feishu.cn"
    port = 443

    result = {"dns_ok": False, "tcp_ok": False, "error": None}

    try:
        ip = socket.getaddrinfo(host, port, socket.AF_INET, socket.SOCK_STREAM)
        result["dns_ok"] = True

        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5)
        sock.connect((host, port))
        sock.close()
        result["tcp_ok"] = True
    except socket.gaierror:
        result["error"] = f"DNS 解析失败: {host}，请检查网络连接"
    except socket.timeout:
        result["error"] = f"TCP 连接超时: {host}:{port}，可能需要配置代理/防火墙"
    except Exception as e:
        result["error"] = f"连接失败: {str(e)}"

    return result


async def main():
    print("=" * 60)
    print("  科吉飞书对接诊断工具")
    print("=" * 60)

    # 1. 环境变量
    print("\n📋 1. 环境变量检查 (.env)")
    print("-" * 40)
    env = check_env()
    for var, info in env.items():
        status = "✅" if info["set"] else "❌"
        print(f"  {status} {var}: {info['value']}")

    # 2. 飞书 SDK
    print("\n📦 2. 飞书 SDK 检查")
    print("-" * 40)
    sdk = await check_feishu_sdk()
    if sdk["installed"]:
        print(f"  ✅ lark-oapi 已安装 (v{sdk['version']})")
    else:
        print(f"  ❌ {sdk['error']}")

    # 3. config.yaml 配置
    print("\n⚙️  3. config.yaml 飞书配置")
    print("-" * 40)
    cfg = check_config()
    print(f"  启用状态: {'✅ enabled' if cfg['feishu_enabled'] else '❌ disabled'}")
    print(f"  App ID: {cfg['app_id']}")
    print(f"  App Secret: {'✅ 已设置' if cfg['app_secret_set'] else '❌ 未设置'}")
    print(f"  Domain: {cfg['domain']}")
    print(f"  allow_from: {cfg['allow_from']}")
    print(f"  streaming: {cfg['streaming']}")

    # 4. 网络连通性
    print("\n🌐 4. 网络连通性检查")
    print("-" * 40)
    net = check_websocket_access()
    print(f"  DNS 解析 open.feishu.cn: {'✅' if net['dns_ok'] else '❌'}")
    print(f"  TCP 连接 open.feishu.cn:443: {'✅' if net['tcp_ok'] else '❌'}")
    if net["error"]:
        print(f"  ⚠️  {net['error']}")

    # 5. 飞书 API 连通性
    print("\n🔗 5. 飞书 API 连通性检查")
    print("-" * 40)
    from core.security.secrets import load_app_config as _load
    config = _load()
    feishu_api = await check_feishu_connection(config)
    if feishu_api["api_reachable"]:
        info = feishu_api["bot_info"]
        print(f"  ✅ 飞书 API 可达！")
        print(f"  机器人名称: {info['app_name']}")
        print(f"  Bot Open ID: {info['open_id']}")
    else:
        print(f"  ❌ {feishu_api['error']}")

    # 6. LLM 后端
    print("\n🤖 6. LLM 后端检查")
    print("-" * 40)
    print(f"  默认模型: {cfg['default_model']}")
    llm = await check_llm_backend(config)
    print(f"  Provider: {llm['provider']}")
    print(f"  Base URL: {llm['base_url']}")
    print(f"  Model: {llm['model']}")
    print(f"  API Key: {'✅ 已配置' if llm['api_key_configured'] else '⚠️ 未配置（本地模型可能不需要）'}")
    if llm["reachable"]:
        print(f"  ✅ LLM 后端可达！响应预览: {llm['response_preview']}")
    else:
        print(f"  ❌ LLM 后端不可达: {llm['error']}")

    # 总结
    print("\n" + "=" * 60)
    print("  诊断总结")
    print("=" * 60)

    issues = []
    if not env.get("FEISHU_APP_ID", {}).get("set"):
        issues.append("❌ FEISHU_APP_ID 未设置")
    if not env.get("FEISHU_APP_SECRET", {}).get("set"):
        issues.append("❌ FEISHU_APP_SECRET 未设置")
    if not sdk["installed"]:
        issues.append("❌ lark-oapi 未安装")
    if not cfg["feishu_enabled"]:
        issues.append("❌ channels.feishu.enabled = false")
    if not net["dns_ok"] or not net["tcp_ok"]:
        issues.append("❌ 无法连接 open.feishu.cn（检查防火墙/代理）")
    if not feishu_api["api_reachable"]:
        issues.append("❌ 飞书 API 不可达（检查 App ID/Secret 是否正确）")
    if not llm["reachable"]:
        issues.append(f"❌ LLM 后端不可达: {llm['base_url']}（检查 LLM 服务是否运行）")

    if issues:
        print("\n发现以下问题需要修复：")
        for i, issue in enumerate(issues, 1):
            print(f"  {i}. {issue}")
    else:
        print("\n✅ 所有检查通过！飞书对接应该可以正常工作。")
        print("   如果仍有问题，请检查：")
        print("   1. 飞书开放平台 → 机器人 → 是否启用")
        print("   2. 飞书开放平台 → 事件订阅 → 是否订阅 im.message.receive_v1")
        print("   3. 飞书开放平台 → 权限管理 → 是否添加 im:message 等权限")
        print("   4. 飞书开放平台 → 版本管理与发布 → 是否已发布")

    print()


if __name__ == "__main__":
    asyncio.run(main())
