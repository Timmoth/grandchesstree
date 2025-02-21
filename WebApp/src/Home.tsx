import React from "react";
import NavBar from "./NavBar";
import AboutCard from "./AboutCard";
import { Link } from "react-router-dom";
import GlobalPerformanceChart from "./perft_stats/GlobalPerformanceChart";
import GlobalLeaderboard from "./perft_stats/GlobalLeaderboard";

const Home: React.FC = () => {
  return (
    <>
      <div>
        <NavBar />

        <div className="flex flex-col m-4 space-y-4 mt-20">
          <AboutCard />
          <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700 flex flex-col justify-between items-center space-x-4">
            <span className="text-md font-bold">Want to get involved?</span>
            <div className="flex-1 flex flex-col items-center justify-center space-y-4">
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
          </div>
          <div className="flex flex-col md:flex-row space-x-4 space-y-4">
            <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700 flex flex-col justify-between items-center space-x-4">
              <span className="text-md font-bold">Startpos</span>
              <div className="flex-1 flex flex-col items-center space-y-4">
                <span className="text-xs">
                  The initial position of a standard game of chess.
                </span>
                <span className="text-sm font-semibold">
                  <Link
                    className="font-medium text-blue-600 hover:underline"
                    to="/perft/0/results"
                  >
                    Results table / summary
                  </Link>
                </span>
                <span className="text-xs">Completed perft [0,1,2,3,4,5,6,7,8,9,10,11]</span>

                <div className="flex flex-col text-sm font-medium space-y-1">
                  <span className="text-sm font-semibold">In Progress:</span>
                  <Link
                    className=" text-blue-600 hover:underline"
                    to="/perft/0/12"
                  >
                    perft stats(12)
                  </Link>
                  <Link
                    className=" text-blue-600 hover:underline"
                    to="/perft/nodes/0/12"
                  >
                    perft nodes(12)
                  </Link>
                </div>
              </div>
            </div>

            <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700 flex flex-col justify-between items-center space-x-4">
              <span className="text-md font-bold">Kiwipete</span>
              <div className="flex-1 flex flex-col items-center space-y-4">
                <span className="text-xs">
                  A popular perft position discovered by Peter McKenzie known to
                  have a large branching factor.
                </span>
                <span className="text-sm font-semibold">
                  <Link
                    className="font-medium text-blue-600 hover:underline"
                    to="/perft/1/results"
                  >
                    Results table / summary
                  </Link>
                </span>
                <span className="text-xs">Completed perft [0,1,2,3,4,5,6,7,8,9]</span>
              </div>
            </div>
            <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700 flex flex-col justify-between items-center space-x-4">
              <span className="text-md font-bold">
                SJE's Symmetric Alternative
              </span>
              <div className="flex-1 flex flex-col items-center space-y-4">
                <span className="text-xs">
                  In 2013, Steven James Edwards identified this position, noting
                  that it offers a greater variety of moves while maintaining
                  color symmetry.
                </span>
                <span className="text-sm font-semibold">
                  <Link
                    className="font-medium text-blue-600 hover:underline"
                    to="/perft/2/results"
                  >
                    Results table / summary
                  </Link>
                </span>
                <span className="text-xs">Completed perft [0,1,2,3,4,5,6,7,8,9]</span>
              </div>
            </div>
          </div>
          <GlobalPerformanceChart/>
          <GlobalLeaderboard/>
        </div>
      </div>
    </>
  );
};

export default Home;
