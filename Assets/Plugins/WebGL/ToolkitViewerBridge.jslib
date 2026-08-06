mergeInto(LibraryManager.library, {
  ToolkitViewerSetStatus: function (messagePointer, current, total) {
    if (window.toolkitViewerSetStatus) {
      window.toolkitViewerSetStatus(UTF8ToString(messagePointer), current, total);
    }
  },

  ToolkitViewerSetTitle: function (titlePointer) {
    if (window.toolkitViewerSetTitle) {
      window.toolkitViewerSetTitle(UTF8ToString(titlePointer));
    }
  },

  ToolkitViewerShowReady: function (failedImageCount) {
    if (window.toolkitViewerShowReady) {
      window.toolkitViewerShowReady(failedImageCount);
    }
  },

  ToolkitViewerShowError: function (messagePointer, detailsPointer) {
    if (window.toolkitViewerShowError) {
      window.toolkitViewerShowError(
        UTF8ToString(messagePointer),
        UTF8ToString(detailsPointer)
      );
    }
  }
});
