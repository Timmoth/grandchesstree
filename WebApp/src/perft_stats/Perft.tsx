import React from "react";
import Leaderboard from "./Leaderboard";
import RealtimeStats from "./RealtimeStats";
import { useParams } from "react-router-dom";
import NavBar from "../NavBar";
import AboutCard from "../AboutCard";
import PerformanceChart from "./PerformanceChart";

const Perft: React.FC = () => {
  const { positionId } = useParams<{ positionId: string }>();
  const { depthId } = useParams<{ depthId: string }>();

  // Convert the id to an integer
  const positionIdInt = positionId ? parseInt(positionId, 10) : NaN;
  const depthIdInt = depthId ? parseInt(depthId, 10) : NaN;

  if (isNaN(positionIdInt) || isNaN(depthIdInt)) {
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
            <div className="flex flex-col md:flex-row space-x-4 space-y-4">
              <RealtimeStats positionId={positionIdInt} depth={depthIdInt} />
              <div className="flex-1">
                <Leaderboard positionId={positionIdInt} depth={depthIdInt} />
              </div>
            </div>
            <div className="flex-1">
              <PerformanceChart positionId={positionIdInt} depth={depthIdInt} />
            </div>
          </div>
        </div>
      </div>
    </>
  );
};

export default Perft;
