import { useEffect, useState } from "react";

import Leaderboard from "./Leaderboard";
import RealtimeStats from "./RealtimeStats";
import { useParams } from "react-router-dom";
import NavBar from "./NavBar";
import AboutCard from "./AboutCard";
import PerformanceChart from "./PerformanceChart";

// Define TypeScript interfaces
interface Contributor {
  id: number;
  name: string;
  nodes: number;
  tasks: number;
  compute_time: number;
}

interface ChessData {
  nodes: number;
  captures: number;
  enpassants: number;
  castles: number;
  promotions: number;
  direct_checks: number;
  single_discovered_check: number;
  direct_discovered_check: number;
  double_discovered_check: number;
  total_checks: number;
  direct_checkmate: number;
  single_discovered_checkmate: number;
  direct_discoverd_checkmate: number;
  double_discoverd_checkmate: number;
  total_mates: number;
  total_tasks: number;
  started_at: number;
  finished_at: number;
  contributors: Contributor[];
}

const CompletedPerft: React.FC = () => {
  const { positionId } = useParams<{ positionId: string }>();
  const { depthId } = useParams<{ depthId: string }>();

  // Convert the id to an integer
  const positionIdInt = positionId ? parseInt(positionId, 10) : NaN;
  const depthIdInt = depthId ? parseInt(depthId, 10) : NaN;

  const [data, setData] = useState<ChessData | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch(`/perft_p${positionId}_d${depthId}_total.json`)
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
  }, [positionId, depthId]);

  if (isNaN(positionIdInt) || isNaN(depthIdInt) || loading || error) {
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

          {/* Conditionally render content based on whether idInt is valid */}
          <div className="flex flex-col space-x-4 space-y-4">
          {data && (  <>
            <h2>Chess Stats</h2>
      <p><strong>Nodes:</strong> {data?.nodes}</p>
      <p><strong>Total Captures:</strong> {data?.captures}</p>
      <p><strong>Total Mates:</strong> {data?.total_mates}</p>
      <p><strong>Started At:</strong> {new Date(data?.started_at * 1000).toLocaleString()}</p>
      <p><strong>Finished At:</strong> {new Date(data?.finished_at * 1000).toLocaleString()}</p>
      <h3>Contributors</h3>
      <ul>
        {data?.contributors.map((contributor) => (
          <li key={contributor.id}>
            <strong>{contributor.name}</strong>: {contributor.nodes} nodes, {contributor.tasks} tasks
          </li>
        ))}
      </ul>
          </>)}
          </div>
        </div>
      </div>
    </>
  );
};

export default CompletedPerft;
