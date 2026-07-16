namespace WotOpcUaMapper.Components.Shared
{
    /// <summary>
    /// CSS classes used to render the browse tree. Ported from the UA Cloud Library MW.Blazor tree.
    /// </summary>
    public class TreeStyle
    {
        public static readonly TreeStyle Bootstrap = new()
        {
            ExpandNodeIconClass = "tree-icon-expand",
            CollapseNodeIconClass = "tree-icon-collapse",
            NodeTitleClass = "p-1",
            NodeTitleSelectableClass = "text-body tree-cursor-pointer",
            NodeTitleSelectedClass = "bg-primary text-white",
            NodeTitleDisabledClass = "text-black-50",
            NodeLoadingClass = "tree-icon-loading"
        };

        public string ExpandNodeIconClass { get; set; } = string.Empty;
        public string CollapseNodeIconClass { get; set; } = string.Empty;
        public string NodeTitleClass { get; set; } = string.Empty;
        public string NodeTitleSelectableClass { get; set; } = string.Empty;
        public string NodeTitleSelectedClass { get; set; } = string.Empty;
        public string NodeTitleDisabledClass { get; set; } = string.Empty;
        public string NodeLoadingClass { get; set; } = string.Empty;
    }
}
