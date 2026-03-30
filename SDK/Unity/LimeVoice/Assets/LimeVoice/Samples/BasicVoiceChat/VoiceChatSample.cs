using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using LimeVoice;

// Minimal voice chat demo.
// 1. Add a GameObject with this script + LimeVoiceClient attached.
// 2. Fill in the Inspector fields (no Token needed — fetched automatically).
// 3. Press Play.
public class VoiceChatSample : MonoBehaviour
{
    [Header("Voice Server")]
    public string Host = "127.0.0.1";
    public int    Port = 7848;

    [Header("API Server")]
    public string ApiBase  = "http://127.0.0.1:8848"; // LimeVoice HTTP port
    public string AppKey   = "your-app-key";           // app_key from app creation

    [Header("Identity")]
    public string AppId  = "a1b2c3d4";
    public string RoomId = "room_1";
    public string UserId = "player_001";
    public int    TokenTtlSeconds = 3600;

    [Header("UI (optional)")]
    public Text StatusText;

    private LimeVoiceClient _client;

    private void Start()
    {
        _client = GetComponent<LimeVoiceClient>();

        _client.OnConnected    += ()     => Log("Connected");
        _client.OnJoined       += ()     => Log("Joined room — voice active");
        _client.OnDisconnected += ()     => Log("Disconnected");
        _client.OnUserJoined   += uid    => Log($"User joined: {uid}");
        _client.OnUserLeft     += uid    => Log($"User left: {uid}");
        _client.OnError        += (c, m) => Log($"Error {c}: {m}");

        _client.Connect(Host, Port);
        StartCoroutine(FetchTokenAndJoin());
    }

    private IEnumerator FetchTokenAndJoin()
    {
        Log("Fetching token...");

        string url  = $"{ApiBase}/api/v1/apps/{AppId}/token";
        string body = $"{{\"user_id\":\"{UserId}\",\"room_id\":\"{RoomId}\",\"ttl_seconds\":{TokenTtlSeconds}}}";

        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("X-App-Key", AppKey);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Log($"Token fetch failed: {req.error}");
            yield break;
        }

        string token = ParseTokenField(req.downloadHandler.text);
        if (string.IsNullOrEmpty(token))
        {
            Log("Token parse failed");
            yield break;
        }

        Log("Token OK, joining room...");
        _client.JoinRoom(AppId, RoomId, UserId, token);
    }

    // Minimal JSON field extractor — avoids a full JSON library dependency.
    private static string ParseTokenField(string json)
    {
        const string key = "\"token\":\"";
        int start = json.IndexOf(key);
        if (start < 0) return null;
        start += key.Length;
        int end = json.IndexOf('"', start);
        return end < 0 ? null : json.Substring(start, end - start);
    }

    // Toggle mute with M key
    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current[UnityEngine.InputSystem.Key.M].wasPressedThisFrame)
#else
        if (Input.GetKeyDown(KeyCode.M))
#endif
        {
            _client.IsMuted = !_client.IsMuted;
            Log(_client.IsMuted ? "Muted" : "Unmuted");
        }
    }

    private void OnDestroy() => _client?.Disconnect();

    private void Log(string msg)
    {
        Debug.Log($"[LimeVoice] {msg}");
        if (StatusText != null) StatusText.text = msg;
    }
}
