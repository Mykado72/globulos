mergeInto(LibraryManager.library, {
  FusionCopyToClipboard: function (textPtr) {
    var text = UTF8ToString(textPtr);
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text);
    }
  },
});