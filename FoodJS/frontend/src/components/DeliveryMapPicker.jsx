import { useEffect, useRef, useState } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import markerIcon2x from "leaflet/dist/images/marker-icon-2x.png";
import markerIcon from "leaflet/dist/images/marker-icon.png";
import markerShadow from "leaflet/dist/images/marker-shadow.png";

const center = [14.5995, 120.9842];
delete L.Icon.Default.prototype._getIconUrl;
L.Icon.Default.mergeOptions({ iconRetinaUrl: markerIcon2x, iconUrl: markerIcon, shadowUrl: markerShadow });
async function places(query, signal) {
  const response = await fetch(`https://nominatim.openstreetmap.org/search?format=jsonv2&limit=5&q=${encodeURIComponent(query)}`, { signal });
  if (!response.ok) throw new Error("Address search is unavailable. You can type the delivery address instead.");
  return response.json();
}
export default function DeliveryMapPicker({ value, onChange }) {
  const container = useRef(null); const map = useRef(null); const marker = useRef(null);
  const callback = useRef(onChange); const controller = useRef(null); const sequence = useRef(0);
  const selectedAddress = useRef(""); const [query, setQuery] = useState(value || "");
  const [results, setResults] = useState([]); const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState("Search for a place, tap the map, or drag the pin. You can also type your delivery address.");
  useEffect(() => { callback.current = onChange; }, [onChange]);
  useEffect(() => {
    const instance = L.map(container.current, { scrollWheelZoom: false }).setView(center, 13);
    const pin = L.marker(center, { draggable: true }).addTo(instance);
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", { maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>' }).addTo(instance);
    map.current = instance; marker.current = pin;
    const fromPin = async latlng => {
      controller.current?.abort(); const request = new AbortController(); controller.current = request;
      const current = ++sequence.current; setBusy(true); setResults([]); setStatus("Looking up the selected address…");
      try {
        const response = await fetch(`https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat=${latlng.lat}&lon=${latlng.lng}`, { signal: request.signal });
        if (!response.ok) throw new Error("Could not look up this pin. Type the address instead.");
        const body = await response.json();
        if (!request.signal.aborted && current === sequence.current) {
          if (!body.display_name) throw new Error("No address was found for this pin. Type the address instead.");
          selectedAddress.current = body.display_name; callback.current(body.display_name); setQuery(body.display_name); setStatus("Delivery address updated from the pin.");
        }
      } catch (failure) { if (!request.signal.aborted && current === sequence.current) setStatus(failure.message); }
      finally { if (current === sequence.current) setBusy(false); }
    };
    pin.on("dragend", () => fromPin(pin.getLatLng()));
    instance.on("click", event => { pin.setLatLng(event.latlng); fromPin(event.latlng); });
    return () => { controller.current?.abort(); instance.remove(); map.current = null; marker.current = null; };
  }, []);
  useEffect(() => {
    setQuery(value || ""); setResults([]); sequence.current++; controller.current?.abort(); setBusy(false);
    if (!value?.trim() || value === selectedAddress.current) return;
    const current = sequence.current; const request = new AbortController(); controller.current = request;
    const timer = setTimeout(async () => {
      try {
        const found = await places(value, request.signal); const first = found?.[0];
        if (!request.signal.aborted && current === sequence.current && first && map.current) { const coords = [Number(first.lat), Number(first.lon)]; map.current.setView(coords, 15); marker.current.setLatLng(coords); }
      } catch { /* Manual delivery text remains usable if positioning fails. */ }
    }, 650);
    return () => { clearTimeout(timer); request.abort(); };
  }, [value]);
  const search = async () => {
    if (query.trim().length < 3) { setStatus("Enter at least three characters to search."); return; }
    controller.current?.abort(); const request = new AbortController(); controller.current = request; const current = ++sequence.current;
    setBusy(true); setResults([]); setStatus("Searching for places…");
    try {
      const found = await places(query.trim(), request.signal);
      if (!request.signal.aborted && current === sequence.current) { setResults(Array.isArray(found) ? found.slice(0, 5) : []); setStatus(found?.length ? "Choose a search result to update the delivery address." : "No places found. Try another search or type the address."); }
    } catch (failure) { if (!request.signal.aborted && current === sequence.current) setStatus(failure.message); }
    finally { if (current === sequence.current) setBusy(false); }
  };
  const choose = place => {
    sequence.current++; controller.current?.abort(); selectedAddress.current = place.display_name;
    const coords = [Number(place.lat), Number(place.lon)]; map.current?.setView(coords, 15); marker.current?.setLatLng(coords);
    callback.current(place.display_name); setQuery(place.display_name); setResults([]); setBusy(false); setStatus("Delivery address updated from the search result.");
  };
  return <div className="delivery-map-picker"><label htmlFor="checkout-map-search">Find a delivery location</label><div className="delivery-map-search-row">
    <input id="checkout-map-search" type="search" value={query} placeholder="Search for a street or landmark" onChange={event => { sequence.current++; controller.current?.abort(); setBusy(false); setResults([]); setQuery(event.target.value); }} onKeyDown={event => { if (event.key === "Enter") { event.preventDefault(); search(); } if (event.key === "Escape") setResults([]); }} />
    <button type="button" disabled={busy} onClick={search}>{busy ? "Searching…" : "Search"}</button>
  </div>{!!results.length && <ul className="delivery-map-results" aria-label="Address search results">{results.map(place => <li key={place.place_id}><button type="button" onClick={() => choose(place)}>{place.display_name}</button></li>)}</ul>}
  <div ref={container} className="delivery-map-canvas" aria-label="Delivery map: tap to position the pin" /><p className="delivery-map-status" role="status">{status}</p></div>;
}
