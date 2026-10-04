mergeInto(LibraryManager.library, {
  PlayMusic: function (urlPtr, loop) {
    window.playMusic(UTF8ToString(urlPtr), loop !== 0);
  },
  StopMusic: function () {
    window.stopMusic();
  },
  SetMusicVolume: function (volume) {
    window.setMusicVolume(volume);
  }
});