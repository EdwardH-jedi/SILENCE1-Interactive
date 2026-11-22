mergeInto(LibraryManager.library, {
  SilenceWindowChanged: function (closure, sound, handleX, handleY, sectionView, firstSeat) {
    window.dispatchEvent(new CustomEvent('sashchange', { detail: { closure: closure, sound: !!sound, handleX: handleX, handleY: handleY,
      sectionView: !!sectionView, firstSeat: !!firstSeat } }));
  }
});
