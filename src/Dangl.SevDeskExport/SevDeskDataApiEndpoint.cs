using System.Collections.Generic;

namespace Dangl.SevDeskExport
{
    /// <summary>
    /// Represents a single endpoint of the SevDesk API from which to obtain data
    /// </summary>
    public class SevDeskDataApiEndpoint
    {
        /// <summary>
        /// The name of the model
        /// </summary>
        public string ModelName { get; set; }

        /// <summary>
        /// The relative url of this resource
        /// </summary>
        public string RelativeUrl { get; set; }

        /// <summary>
        /// Any additional query parameters to be included in each request
        /// </summary>
#pragma warning disable CA2227 // Collection properties should be read only
        public Dictionary<string, string> AdditionalParameters { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only

        /// <summary>
        /// If this is set, the current endpoint depends on another one and will use the base ids to
        /// obtain its own values
        /// </summary>
        public PathReplacement PathReplacement { get; set; }
    }
}
