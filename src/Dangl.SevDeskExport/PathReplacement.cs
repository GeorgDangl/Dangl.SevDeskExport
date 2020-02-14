namespace Dangl.SevDeskExport
{
    /// <summary>
    /// This class defines how a dependent resources' url should be modelled
    /// </summary>
    public class PathReplacement
    {
        /// <summary>
        /// The name of the base model
        /// </summary>
        public string BaseModel { get; set; }

        /// <summary>
        /// The parameter name in the url, e.g. 'id' means that the '{id}' segment in an url will be replaced
        /// </summary>
        public string UrlParameterName { get; set; }

        /// <summary>
        /// The name of the property in the base model that should be used for replacement
        /// </summary>
        public string ObjectProperty { get; set; }
    }
}
