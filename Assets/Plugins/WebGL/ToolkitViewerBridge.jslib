mergeInto(LibraryManager.library, {
  ToolkitAudioUpdate: function (pointer) {
    if (window.toolkitAudio) window.toolkitAudio.update(JSON.parse(UTF8ToString(pointer)));
  },
  ToolkitAudioRemove: function (pointer) {
    if (window.toolkitAudio) window.toolkitAudio.remove(UTF8ToString(pointer));
  },
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
  },

  ToolkitEditorImageAdded: function (filenamePointer) {
    if (window.toolkitEditorImageAdded) {
      window.toolkitEditorImageAdded(UTF8ToString(filenamePointer));
    }
  },

  ToolkitEditorSetComposePrompt: function (messagePointer) {
    if (window.toolkitEditorSetComposePrompt) {
      window.toolkitEditorSetComposePrompt(UTF8ToString(messagePointer));
    }
  },

  ToolkitEditorSavePayload: function (payloadPointer) {
    if (window.toolkitEditorSavePayload) {
      window.toolkitEditorSavePayload(UTF8ToString(payloadPointer));
    }
  },

  ToolkitEditorSceneChanged: function () {
    if (window.toolkitEditorSceneChanged) {
      window.toolkitEditorSceneChanged();
    }
  },

  ToolkitEditorSetRotationMode: function (active) {
    if (window.toolkitEditorSetRotationMode) {
      window.toolkitEditorSetRotationMode(active !== 0);
    }
  }
});
