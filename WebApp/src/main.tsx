import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import Home from "./Home";
import PerftPage from "./perft_stats/Perft";
import PerftResults from "./Results/PerftResults";
import PerftNodes from "./perft_nodes/PerftNodes";
import CreateAccountForm from "./accounts/CreateAccountForm";
import Account from "./accounts/Account";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        {/* Define your routes here */}
        <Route path="/" element={<Home />} /> {/* Home page route */}
        <Route path="/perft/:positionId/:depthId" element={<PerftPage />} />{" "}
        <Route path="/perft/:positionId/results" element={<PerftResults />} />{" "}
        <Route path="/perft/nodes/:positionId/:depthId" element={<PerftNodes />} />{" "}
        <Route path="/accounts/signup/" element={<CreateAccountForm />} />{" "}
        <Route path="/accounts/:accountId/" element={<Account />} />{" "}

        {/* Perft page with dynamic id */}
      </Routes>
    </BrowserRouter>
  </StrictMode>
);
