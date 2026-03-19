/**
 * Cloudflare Worker: YMM4 Game Hub Backend
 * 
 * 役割:
 * 1. WebSocket ルーム管理 (Durable Objects)
 * 2. ゲーム配信レジストリ (GitHub Releases Proxy + Cache)
 * 3. ランダムマッチング (Durable Objects)
 */

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // 1. WebSocket ルーム管理
    if (url.pathname === "/ws") {
      const roomId = url.searchParams.get("roomId");
      if (!roomId) return new Response("Missing roomId", { status: 400 });

      const id = env.ROOM_MANAGER.idFromName(roomId);
      const obj = env.ROOM_MANAGER.get(id);
      return obj.fetch(request);
    }

    // 2. ゲームレジストリ取得
    if (url.pathname === "/games") {
      return handleGamesRequest(request, env);
    }

    // 3. ランダムマッチング
    if (url.pathname.startsWith("/matchmaking")) {
      const id = env.MATCHMAKING_MANAGER.idFromName("global");
      const obj = env.MATCHMAKING_MANAGER.get(id);
      return obj.fetch(request);
    }

    return new Response("Not Found", { status: 404 });
  }
};

/**
 * GitHub Releases から情報を取得し、キャッシュして返却する
 */
async function handleGamesRequest(request, env) {
  const cacheUrl = new URL(request.url);
  const cacheKey = new Request(cacheUrl.toString(), request);
  const cache = caches.default;

  let response = await cache.match(cacheKey);
  if (response) return response;

  const GITHUB_REPO = "[YOUR_GITHUB_REPO]/[YOUR_REPO_NAME]"; // TODO: 自身のレポジトリに書き換えてください
  const githubApiUrl = `https://api.github.com/repos/${GITHUB_REPO}/releases`;

  try {
    const ghResponse = await fetch(githubApiUrl, {
      headers: { "User-Agent": "YMM4-GameHub-Worker" }
    });

    if (!ghResponse.ok) return new Response("GitHub API Error", { status: ghResponse.status });

    const releases = await ghResponse.json();
    const games = releases.map(release => {
      const dllAsset = release.assets.find(a => a.name.endsWith(".dll"));
      const thumbAsset = release.assets.find(a => a.name.match(/\.(png|jpg|jpeg|webp)$/i));
      const assemblyName = dllAsset ? dllAsset.name.replace(/\.dll$/i, "") : "";

      return {
        Id: assemblyName,
        Name: release.name || release.tag_name,
        Description: release.body || "",
        Version: release.tag_name,
        Author: release.author.login,
        Category: "CardGame",
        MinPlayers: 2,
        MaxPlayers: 2,
        DownloadUrl: dllAsset ? dllAsset.browser_download_url : "",
        ThumbnailUrl: thumbAsset ? thumbAsset.browser_download_url : "https://via.placeholder.com/220x140?text=No+Image",
        DllFileName: dllAsset ? dllAsset.name : ""
      };
    }).filter(g => g.DownloadUrl !== "");

    const body = JSON.stringify({ Games: games });
    response = new Response(body, {
      headers: {
        "Content-Type": "application/json",
        "Access-Control-Allow-Origin": "*",
        "Cache-Control": "public, max-age=3600",
      }
    });

    return response;
  } catch (err) {
    return new Response(err.stack, { status: 500 });
  }
}

// Durable Objects: RoomManager
export class RoomManager {
  constructor(state, env) {
    this.state = state;
    this.sessions = new Set();
  }

  async fetch(request) {
    if (request.headers.get("Upgrade") !== "websocket") {
      return new Response("Expected WebSocket", { status: 400 });
    }

    const [client, server] = Object.values(new WebSocketPair());
    server.accept();

    // 新規参加があったことを既存の全員に通知
    const joinMsg = JSON.stringify({ 
      senderId: "server", 
      data: { type: "system:joined", count: this.sessions.size + 1 } 
    });
    for (const s of this.sessions) {
      try {
        s.send(joinMsg);
      } catch (e) {}
    }

    this.sessions.add(server);

    server.addEventListener("message", ev => {
      try {
        const msg = JSON.parse(ev.data);
        
        // サーバー宛ての Ping 処理
        if (msg.targetId === "server" && msg.data?.type === "ping") {
          server.send(JSON.stringify({
            senderId: "server",
            targetId: msg.senderId,
            data: { type: "pong" }
          }));
          return;
        }

        // 全員（自分以外）に転送
        for (const s of this.sessions) {
          if (s !== server) {
            try {
              s.send(ev.data);
            } catch (e) {
              this.sessions.delete(s);
            }
          }
        }
      } catch (e) {
        // 非JSONメッセージなどはそのまま転送を試みる
        for (const s of this.sessions) {
          if (s !== server) {
            try { s.send(ev.data); } catch (ex) { this.sessions.delete(s); }
          }
        }
      }
    });

    server.addEventListener("close", () => {
      this.sessions.delete(server);
    });

    return new Response(null, { status: 101, webSocket: client });
  }
}

// Durable Objects: MatchmakingManager
export class MatchmakingManager {
  constructor(state, env) {
    this.state = state;
    this.lock = Promise.resolve();
    this.sessions = new Set(); // Lobby sessions
  }

  async fetch(request) {
    const url = new URL(request.url);
    
    // WebSocket (ロビー接続)
    if (url.pathname.endsWith("/ws")) {
      const playerId = url.searchParams.get("playerId") || "anon";
      const [client, server] = Object.values(new WebSocketPair());
      server.accept();
      server.playerId = playerId;
      this.sessions.add(server);
      server.addEventListener("close", () => this.sessions.delete(server));
      await this.broadcastCounts([server]);
      return new Response(null, { status: 101, webSocket: client });
    }

    const result = await (this.lock = this.lock.then(() => this.handleRequest(url)));
    return result;
  }

  async broadcastCounts(targets = this.sessions) {
    const targetCount = (targets instanceof Set) ? targets.size : (Array.isArray(targets) ? targets.length : 0);
    if (targetCount === 0) return;

    const storage = await this.state.storage.list({ prefix: "match:" });
    const counts = {};
    const now = Date.now();
    const TIMEOUT = 600000;
    let totalMatching = 0;

    for (const [key, value] of storage.entries()) {
      const gameId = key.split(":")[1];
      if (value && (now - value.timestamp < TIMEOUT)) {
        counts[gameId] = (counts[gameId] || 0) + 1;
        totalMatching++;
      }
    }

    const uniquePlayers = new Set();
    for (const s of this.sessions) {
      if (s.playerId) uniquePlayers.add(s.playerId);
    }

    const message = JSON.stringify({ 
      type: "lobby_counts", 
      counts, 
      totalMatching,
      totalOnline: uniquePlayers.size
    });
    for (const s of targets) {
      try {
        s.send(message);
      } catch (e) {
        this.sessions.delete(s);
      }
    }
  }

  async handleRequest(url) {
    const gameId = url.searchParams.get("gameId");
    const playerId = url.searchParams.get("playerId");

    if (!gameId) return new Response("Missing gameId", { status: 400 });

    const key = `match:${gameId}`;
    const now = Date.now();
    const TIMEOUT = 600000;

    if (url.pathname.endsWith("/leave")) {
      await this.state.storage.delete(key);
      await this.broadcastCounts();
      return new Response(JSON.stringify({ success: true }), {
        headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" }
      });
    }

    if (url.pathname.endsWith("/status")) {
      const waiting = await this.state.storage.get(key);
      const count = (waiting && (now - waiting.timestamp < TIMEOUT)) ? 1 : 0;
      return new Response(JSON.stringify({ waitingCount: count }), {
        headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" }
      });
    }

    const waiting = await this.state.storage.get(key);
    if (waiting && (now - waiting.timestamp < TIMEOUT)) {
      if (playerId && waiting.playerId === playerId) {
        console.log(`Self-match detected for ${playerId}. Refreshing.`);
        waiting.timestamp = now;
        await this.state.storage.put(key, waiting);
        return new Response(JSON.stringify({ roomId: waiting.roomId, isHost: true }), {
          headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" }
        });
      }

      // Match found
      await this.state.storage.delete(key);
      
      // ホストに通知
      for (const s of this.sessions) {
        if (s.playerId === waiting.playerId) {
          try {
            s.send(JSON.stringify({ type: "match_found", gameId, roomId: waiting.roomId }));
          } catch (e) {}
        }
      }

      await this.broadcastCounts();
      return new Response(JSON.stringify({ roomId: waiting.roomId, isHost: false }), {
        headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" }
      });
    }

    // New host
    const newRoomId = Math.random().toString(36).substring(2, 8);
    const newWaiting = { roomId: newRoomId, playerId: playerId || "unknown", timestamp: now };
    await this.state.storage.put(key, newWaiting);
    await this.broadcastCounts();

    return new Response(JSON.stringify({ roomId: newRoomId, isHost: true }), {
      headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" }
    });
  }
}
