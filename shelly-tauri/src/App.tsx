import { useState, useEffect } from "react";
import { TerminalView } from "./components/TerminalView";
import { getCurrentWindow } from "@tauri-apps/api/window";
import "./App.css";

function App() {
  const [sessionId] = useState(() => crypto.randomUUID());

  useEffect(() => {
    getCurrentWindow().show();
  }, []);

  return (
    <div className="app">
      <TerminalView sessionId={sessionId} />
    </div>
  );
}

export default App;
