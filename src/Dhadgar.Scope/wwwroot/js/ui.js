// Simple UI helpers for the Scope site.
(function () {
  function lockScroll() {
    const body = document.body;
    if (body.dataset.scopeScrollLocked === "1") return;

    const y = window.scrollY || window.pageYOffset || 0;
    body.dataset.scopeScrollY = String(y);
    body.dataset.scopeScrollLocked = "1";

    // Freeze the page in-place (prevents background scrolling on iOS).
    body.style.position = "fixed";
    body.style.top = `-${y}px`;
    body.style.left = "0";
    body.style.right = "0";
    body.style.width = "100%";
    body.style.overflow = "hidden";
    body.style.touchAction = "none";
  }

  function unlockScroll() {
    const body = document.body;
    if (body.dataset.scopeScrollLocked !== "1") return;

    const y = parseInt(body.dataset.scopeScrollY || "0", 10) || 0;

    body.style.position = "";
    body.style.top = "";
    body.style.left = "";
    body.style.right = "";
    body.style.width = "";
    body.style.overflow = "";
    body.style.touchAction = "";

    delete body.dataset.scopeScrollLocked;
    delete body.dataset.scopeScrollY;

    window.scrollTo(0, y);
  }

  window.scopeUi = {
    setScrollLock: function (locked) {
      if (locked) lockScroll();
      else unlockScroll();
    }
  };
})();
