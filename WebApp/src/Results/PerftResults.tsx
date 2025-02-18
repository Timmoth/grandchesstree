import React, { useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import NavBar from "../NavBar";
import AboutCard from "../AboutCard";
import { PerftSummary } from "./models/PerftSummary";
import PerftResultsTable from "./PerftResultsTable";
import PerftStatsTable from "./PerftStatsTable";
import UniquePositionsTable from "./UniquePositionsTable";

const PerftResults: React.FC = () => {
  const { positionId } = useParams<{ positionId: string }>();

  // Convert the id to an integer
  const positionIdInt = positionId ? parseInt(positionId, 10) : NaN;

  const [data, setData] = useState<PerftSummary | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const formatBigNumber = (num: number): string => {
    if (num >= 1e12) return (num / 1e12).toFixed(1) + "t"; // Trillion
    if (num >= 1e9) return (num / 1e9).toFixed(1) + "b"; // Billion
    if (num >= 1e6) return (num / 1e6).toFixed(1) + "m"; // Million
    if (num >= 1e3) return (num / 1e3).toFixed(1) + "k"; // Thousand
    return num.toString();
  };
  const formatDuration = (seconds: number): string => {
    const days = Math.floor(seconds / 86400);
    const hours = Math.floor((seconds % 86400) / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = seconds % 60;
  
    if (days > 0) {
      return `${days}d ${hours}h`;
    }else if(hours > 1){
      return `${hours}h ${minutes}m`;
    }else{
      return `${minutes}m ${secs}s`;
    }
  };
  useEffect(() => {
    fetch(`/perft_p${positionId}_results.json`)
      .then((response) => {
        if (!response.ok) {
          throw new Error("Failed to fetch data");
        }
        return response.json();
      })
      .then((jsonData) => {
        setData(jsonData);
        setLoading(false);
      })
      .catch((err) => {
        setError(err.message);
        setLoading(false);
      });
  }, [positionId]);
  
  if (loading || error || !data) {
    return (
      <>
        <div>
          Loading...
        </div>
      </>
    );
  }
  
  if (isNaN(positionIdInt)) {
    return (
      <>
        <div>
          <NavBar />
          <div className="flex flex-col m-4 space-y-4 mt-20">
            <AboutCard />
          </div>
        </div>
      </>
    );
  }
  return (
    <>
      <div>
        <NavBar />
        <div className="flex flex-col m-4 space-y-4 mt-20">
        <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700 flex flex-col justify-between items-center space-x-4">
            <span className="text-md font-bold">{data.position_name}</span>
            <span className="text-sm font-semibold">
              {data.position_description}
            </span>
            <span className="text-xs">
              Completed perft [{data.results.map(r => r.depth).join(", ")}]
            </span>
            <span className="text-xs">
              Total Nodes [{formatBigNumber(data.results.reduce((sum, r) => sum + r.nodes, 0))}]
            </span>
            <span className="text-xs">
              Total Duration [{formatDuration(data.results.reduce((sum, r) => sum + (r.finished_at - r.started_at), 0))}]
            </span>
            <span className="text-xs">
              {data.position_fen}
            </span>
          </div>
        
        <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700 flex flex-col justify-between items-center space-x-4">
            <span className="text-md font-bold">Want to get involved?</span>
            <span className="text-sm font-semibold">
              If you're interested in volunteering computing resources or
              collaborating on the project
            </span>
            <span className="text-sm font-semibold">
              <a
                className="font-medium text-blue-600 hover:underline"
                href="https://discord.gg/cTu3aeCZVe"
                target="_blank"
                rel="noopener noreferrer"
              >
                join the Discord server!
              </a>
            </span>
          </div>
          
        <PerftResultsTable summary={data}/>
        <div className="flex flex-col xl:flex-row space-x-4 space-y-4">
        <PerftStatsTable summary={data}/>
        <UniquePositionsTable summary={data}/>
        </div>
        </div>
      </div>
    </>
  );
};

export default PerftResults;
