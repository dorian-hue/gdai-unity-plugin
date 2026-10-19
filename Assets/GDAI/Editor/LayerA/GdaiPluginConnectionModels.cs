using System;
using System.Collections.Generic;

// =====================================================================================
// GDAI Unity Plugin · MVP-C · device-pairing connection DTOs (unity-plugin-connection).
// Parsed with Newtonsoft.Json (Editor asmdef references Unity.Newtonsoft.Json).
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    [Serializable]
    public class GdaiConnectionStartResponse
    {
        public string device_code;      // secret — plugin only, never shown to user
        public string poll_secret;      // secret — plugin only, never shown to user
        public string user_code;        // short human-readable code shown in the approval page
        public string verification_url;
        public int expires_in;          // seconds
        public int interval;            // seconds between polls
    }

    [Serializable]
    public class GdaiConnectionPollResponse
    {
        public string status;               // "pending" | "approved" | "consumed" | "denied" | "expired"
        public string connection_token;     // gdai_plugin_v1.* — returned once on first approved poll
        public string expires_at;
        public List<GdaiConnectionProject> projects = new List<GdaiConnectionProject>();
    }

    [Serializable]
    public class GdaiConnectionProject
    {
        public string project_id;
        public string name;
    }
}
