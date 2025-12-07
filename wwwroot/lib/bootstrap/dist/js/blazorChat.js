window.blazorChat = {
  scrollToBottom: function (element) {
    if (!element) return;
    try {
      element.scrollTop = element.scrollHeight;
    } catch (e) {
      // noop
    }
  }
};
