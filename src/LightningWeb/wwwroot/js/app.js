var STRIKES_URL = "/api/strikes";
var POLL_INTERVAL_MS = 10000;
var MARKER_SCALE = 12;
var map = null;
var pageController = null;

function formatStrike(strike) {
  return "Intensity " + strike.intensity + ", latitude " + strike.latitude + ", longitude " + strike.longitude;
}

function renderStrikeListText(strikes) {
  if (!strikes || strikes.length === 0) {
    return "No current strikes";
  }
  var lines = [];
  for (var i = 0; i < strikes.length; i++) {
    lines.push(formatStrike(strikes[i]));
  }
  return lines.join("\n");
}

function createController(options) {
  var state = { strikes: [], error: "", paused: false };
  var markers = [];
  options = options || {};

  function notify() {
    if (options.render) {
      options.render(state);
    }
  }

  function clearMarkers() {
    for (var i = 0; i < markers.length; i++) {
      markers[i].setMap(null);
    }
    markers = [];
  }

  function draw(strikes) {
    clearMarkers();
    if (!options.createMarker) {
      return;
    }
    for (var i = 0; i < strikes.length; i++) {
      var strike = strikes[i];
      markers.push(options.createMarker({
        id: strike.id,
        latitude: strike.latitude,
        longitude: strike.longitude,
        label: String(strike.intensity),
        scale: MARKER_SCALE
      }));
    }
  }

  function onSuccess(strikes) {
    state.strikes = strikes ? strikes.slice() : [];
    state.error = "";
    draw(state.strikes);
    notify();
  }

  function onFailure() {
    state.error = "Strike updates are unavailable. Showing the last successful list.";
    notify();
  }

  function redraw() {
    draw(state.strikes);
  }

  function onTimer() {
    if (state.paused || !options.fetchStrikes) {
      return;
    }
    var result;
    try {
      result = options.fetchStrikes();
    } catch (err) {
      onFailure();
      return;
    }
    if (result && typeof result.then === "function") {
      result.then(onSuccess, onFailure);
    } else {
      onSuccess(result);
    }
  }

  return {
    getState: function () { return state; },
    getMarkers: function () { return markers; },
    onSuccess: onSuccess,
    onFailure: onFailure,
    onTimer: onTimer,
    pause: function () { state.paused = true; notify(); },
    resume: function () { state.paused = false; notify(); },
    redraw: redraw,
    buttonName: function () { return state.paused ? "Resume updates" : "Pause updates"; }
  };
}

function initMap() {
  if (typeof google === "undefined" || typeof document === "undefined") {
    return;
  }
  var element = document.getElementById("map");
  if (!element) {
    return;
  }
  map = new google.maps.Map(element, {
    center: { lat: 0, lng: 0 },
    zoom: 2
  });
  if (pageController) {
    pageController.redraw();
  }
}

function createMarker(spec) {
  if (map === null || typeof google === "undefined") {
    return { setMap: function () {} };
  }
  return new google.maps.Marker({
    map: map,
    position: { lat: spec.latitude, lng: spec.longitude },
    label: spec.label,
    clickable: false,
    icon: {
      path: google.maps.SymbolPath.CIRCLE,
      scale: spec.scale,
      fillColor: "#f4c430",
      fillOpacity: 1,
      strokeColor: "#ffffff",
      strokeWeight: 1
    }
  });
}

function render(state) {
  if (typeof document === "undefined") {
    return;
  }
  var list = document.getElementById("strike-items");
  var empty = document.getElementById("empty-strikes");
  var error = document.getElementById("poll-error");
  var button = document.getElementById("pause-updates");
  if (error) {
    error.textContent = state.error;
  }
  if (button) {
    button.textContent = state.paused ? "Resume updates" : "Pause updates";
  }
  if (!list || !empty) {
    return;
  }
  while (list.firstChild) {
    list.removeChild(list.firstChild);
  }
  if (!state.strikes || state.strikes.length === 0) {
    empty.hidden = false;
    empty.textContent = "No current strikes";
    return;
  }
  empty.hidden = true;
  empty.textContent = "";
  for (var i = 0; i < state.strikes.length; i++) {
    var item = document.createElement("li");
    item.textContent = formatStrike(state.strikes[i]);
    list.appendChild(item);
  }
}

function fetchStrikes() {
  return fetch(STRIKES_URL).then(function (response) {
    if (!response.ok) {
      throw new Error("strikes failed");
    }
    return response.json();
  });
}

function startPage() {
  var button = document.getElementById("pause-updates");
  pageController = createController({
    fetchStrikes: fetchStrikes,
    createMarker: createMarker,
    render: render
  });
  if (button) {
    button.addEventListener("click", function () {
      if (pageController.getState().paused) {
        pageController.resume();
      } else {
        pageController.pause();
      }
    });
  }
  pageController.onTimer();
  window.setInterval(function () {
    pageController.onTimer();
  }, POLL_INTERVAL_MS);
}

function boot() {
  if (typeof document === "undefined" || document === null) {
    return;
  }
  startPage();
}

boot();
