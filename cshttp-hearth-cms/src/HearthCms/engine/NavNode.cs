using System.Collections.Generic;
using Newtonsoft.Json;

namespace System.engine
{
    // One navigation menu node. Children is only meaningful on top-level nodes
    // (the menu is at most two levels deep; see NavMenu.Normalize).
    public class NavNode
    {
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("children")] public List<NavNode> Children { get; set; }
    }
}
