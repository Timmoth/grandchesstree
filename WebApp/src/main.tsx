import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import Home from "./Home";
import PerftPage from "./Perft";
import CompletedPerft from "./CompletedPerft";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        {/* Define your routes here */}
        <Route path="/" element={<Home />} /> {/* Home page route */}
        <Route path="/perft/:positionId/:depthId" element={<PerftPage />} />{" "}
        <Route path="/perft/:positionId/:depthId/results" element={<CompletedPerft />} />{" "}

        {/* Perft page with dynamic id */}
      </Routes>
    </BrowserRouter>
  </StrictMode>
);
