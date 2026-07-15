using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.McpBridge
{
    [Serializable]
    public class BridgeRequest
    {
        public string method;
        public JObject @params;
    }

    [Serializable]
    public class BridgeResponse
    {
        public object result;
        public string error;
    }

    [InitializeOnLoad]
    public static class McpBridgeServer
    {
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static readonly Dictionary<string, Func<JObject, object>> _handlers = new();
        private static readonly Queue<Action> _mainThreadQueue = new();
        private static bool _running;

        static McpBridgeServer()
        {
            if (!McpBridgeConfig.Enabled) return;
            EditorApplication.update += ProcessMainThreadQueue;
            Start();
        }

        public static void RegisterHandler(string method, Func<JObject, object> handler)
        {
            _handlers[method] = handler;
        }

        public static void Start()
        {
            if (_running) return;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{McpBridgeConfig.Port}/");
                _listener.Start();
                _running = true;

                _listenerThread = new Thread(ListenLoop) { IsBackground = true };
                _listenerThread.Start();

                Debug.Log($"[McpBridge] Server started on port {McpBridgeConfig.Port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[McpBridge] Failed to start: {ex.Message}");
            }
        }

        public static void Stop()
        {
            _running = false;
            _listener?.Stop();
            _listener?.Close();
            _listenerThread = null;
            Debug.Log("[McpBridge] Server stopped");
        }

        private static void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException) when (!_running)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_running) Debug.LogError($"[McpBridge] Listener error: {ex.Message}");
                }
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            var response = context.Response;
            response.ContentType = "application/json";

            try
            {
                using var reader = new StreamReader(context.Request.InputStream);
                var body = reader.ReadToEnd();
                var request = JsonConvert.DeserializeObject<BridgeRequest>(body);

                if (request?.method == null)
                {
                    WriteResponse(response, new BridgeResponse { error = "Missing 'method' field" });
                    return;
                }

                // ping is handled directly on background thread
                if (request.method == "ping")
                {
                    WriteResponse(response, new BridgeResponse
                    {
                        result = new
                        {
                            unityVersion = Application.unityVersion,
                            projectPath = Application.dataPath,
                            branch = GetGitBranch()   // current checkout; null if undetermined
                        }
                    });
                    return;
                }

                if (!_handlers.TryGetValue(request.method, out var handler))
                {
                    WriteResponse(response, new BridgeResponse { error = $"Unknown method: {request.method}" });
                    return;
                }

                // Execute handler on main thread (Unity API requirement)
                object handlerResult = null;
                string handlerError = null;
                var done = new ManualResetEventSlim(false);

                _mainThreadQueue.Enqueue(() =>
                {
                    try
                    {
                        handlerResult = handler(request.@params ?? new JObject());
                    }
                    catch (Exception ex)
                    {
                        handlerError = ex.Message;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                // Wait up to 25 seconds for main thread to process
                if (!done.Wait(TimeSpan.FromSeconds(25)))
                {
                    WriteResponse(response, new BridgeResponse { error = "Main thread execution timed out" });
                    return;
                }

                WriteResponse(response, new BridgeResponse { result = handlerResult, error = handlerError });
            }
            catch (Exception ex)
            {
                WriteResponse(response, new BridgeResponse { error = ex.Message });
            }
        }

        private static void WriteResponse(HttpListenerResponse response, BridgeResponse data)
        {
            try
            {
                var json = JsonConvert.SerializeObject(data);
                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[McpBridge] Failed to write response: {ex.Message}");
            }
        }

        private static void ProcessMainThreadQueue()
        {
            while (_mainThreadQueue.Count > 0)
            {
                var action = _mainThreadQueue.Dequeue();
                action?.Invoke();
            }
        }

        // Current git branch of the working copy (for tools that need to warn when
        // the editor isn't on the expected branch). Reads .git/HEAD directly — no
        // git process — and handles worktrees where .git is a file. Returns null on
        // detached HEAD or any read failure. Runs on the listener thread (file IO
        // only), like the rest of the ping response.
        private static string GetGitBranch()
        {
            try
            {
                var root = Directory.GetParent(Application.dataPath)?.FullName;
                if (root == null) return null;

                var gitPath = Path.Combine(root, ".git");
                string headFile;
                if (Directory.Exists(gitPath))
                {
                    headFile = Path.Combine(gitPath, "HEAD");
                }
                else if (File.Exists(gitPath))
                {
                    // Worktree/submodule: ".git" is a file "gitdir: <path>".
                    var line = File.ReadAllText(gitPath).Trim();
                    const string prefix = "gitdir:";
                    if (!line.StartsWith(prefix)) return null;
                    var gitDir = line.Substring(prefix.Length).Trim();
                    if (!Path.IsPathRooted(gitDir)) gitDir = Path.GetFullPath(Path.Combine(root, gitDir));
                    headFile = Path.Combine(gitDir, "HEAD");
                }
                else
                {
                    return null;
                }

                if (!File.Exists(headFile)) return null;
                var head = File.ReadAllText(headFile).Trim();
                const string refPrefix = "ref: refs/heads/";
                return head.StartsWith(refPrefix) ? head.Substring(refPrefix.Length) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
