import React from "react";
import NavBar from "./NavBar";
import AboutCard from "./AboutCard";
import PerftResults from "./PerftResults";
import { Link } from "react-router-dom";
import GlobalPerformanceChart from "./GlobalPerformanceChart";
import GlobalLeaderboard from "./GlobalLeaderboard";

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
            <div className="flex-1 flex flex-col items-center justify-center space-y-4">
              <span className="text-sm font-semibold">
                rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1
              </span>

              <ul className="max-w-md space-y-1 text-gray-500 list-inside dark:text-gray-400">
                <li className="flex items-center">
                  <svg
                    className="w-3.5 h-3.5 me-2 text-green-500  shrink-0"
                    aria-hidden="true"
                    xmlns="http://www.w3.org/2000/svg"
                    fill="currentColor"
                    viewBox="0 0 20 20"
                  >
                    <path d="M10 .5a9.5 9.5 0 1 0 9.5 9.5A9.51 9.51 0 0 0 10 .5Zm3.707 8.207-4 4a1 1 0 0 1-1.414 0l-2-2a1 1 0 0 1 1.414-1.414L9 10.586l3.293-3.293a1 1 0 0 1 1.414 1.414Z" />
                  </svg>
                  <Link
                    className="font-medium rounded-lg text-sm py-2 text-center flex items-center"
                    to="/perft/0/11/results"
                  >
                    perft(11)
                  </Link>
                </li>

                <li className="flex items-center">
                  <svg
                    className="w-3.5 h-3.5 me-2 text-gray-500  shrink-0"
                    aria-hidden="true"
                    xmlns="http://www.w3.org/2000/svg"
                    fill="currentColor"
                    viewBox="0 0 20 20"
                  >
                    <path d="M10 .5a9.5 9.5 0 1 0 9.5 9.5A9.51 9.51 0 0 0 10 .5Zm3.707 8.207-4 4a1 1 0 0 1-1.414 0l-2-2a1 1 0 0 1 1.414-1.414L9 10.586l3.293-3.293a1 1 0 0 1 1.414 1.414Z" />
                  </svg>
                  <Link
                    className="font-medium rounded-lg text-sm py-2 text-center flex items-center"
                    to="/perft/0/12"
                  >
                    perft(12)
                  </Link>
                </li>
              </ul>
            </div>
          </div>
     
          <div className="space-y-4 p-4 bg-gray-100 rounded-lg text-gray-700 flex flex-col justify-between items-center space-x-4">
            <span className="text-md font-bold">Kiwipete</span>
            <div className="flex-1 flex flex-col items-center justify-center space-y-4">
              <span className="text-sm font-semibold">
                r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -
              </span>

              <ul className="max-w-md space-y-1 text-gray-500 list-inside dark:text-gray-400">
                <li className="flex items-center">
                  <svg
                    className="w-3.5 h-3.5 me-2 text-green-500  shrink-0"
                    aria-hidden="true"
                    xmlns="http://www.w3.org/2000/svg"
                    fill="currentColor"
                    viewBox="0 0 20 20"
                  >
                    <path d="M10 .5a9.5 9.5 0 1 0 9.5 9.5A9.51 9.51 0 0 0 10 .5Zm3.707 8.207-4 4a1 1 0 0 1-1.414 0l-2-2a1 1 0 0 1 1.414-1.414L9 10.586l3.293-3.293a1 1 0 0 1 1.414 1.414Z" />
                  </svg>
                  <Link
                    className="font-medium rounded-lg text-sm py-2 text-center flex items-center"
                    to="/perft/1/6/results"
                  >
                    perft(6)
                  </Link>
                </li>
                <li className="flex items-center">
                  <svg
                    className="w-3.5 h-3.5 me-2 text-green-500  shrink-0"
                    aria-hidden="true"
                    xmlns="http://www.w3.org/2000/svg"
                    fill="currentColor"
                    viewBox="0 0 20 20"
                  >
                    <path d="M10 .5a9.5 9.5 0 1 0 9.5 9.5A9.51 9.51 0 0 0 10 .5Zm3.707 8.207-4 4a1 1 0 0 1-1.414 0l-2-2a1 1 0 0 1 1.414-1.414L9 10.586l3.293-3.293a1 1 0 0 1 1.414 1.414Z" />
                  </svg>
                  <Link
                    className="font-medium rounded-lg text-sm py-2 text-center flex items-center"
                    to="/perft/1/7/results"
                  >
                    perft(7)
                  </Link>
                </li>
                <li className="flex items-center">
                  <svg
                    className="w-3.5 h-3.5 me-2 text-green-500  shrink-0"
                    aria-hidden="true"
                    xmlns="http://www.w3.org/2000/svg"
                    fill="currentColor"
                    viewBox="0 0 20 20"
                  >
                    <path d="M10 .5a9.5 9.5 0 1 0 9.5 9.5A9.51 9.51 0 0 0 10 .5Zm3.707 8.207-4 4a1 1 0 0 1-1.414 0l-2-2a1 1 0 0 1 1.414-1.414L9 10.586l3.293-3.293a1 1 0 0 1 1.414 1.414Z" />
                  </svg>
                  <Link
                    className="font-medium rounded-lg text-sm py-2 text-center flex items-center"
                    to="/perft/1/8/results"
                  >
                    perft(8)
                  </Link>
                </li>
                <li className="flex items-center">
                  <svg
                    className="w-3.5 h-3.5 me-2 text-green-500  shrink-0"
                    aria-hidden="true"
                    xmlns="http://www.w3.org/2000/svg"
                    fill="currentColor"
                    viewBox="0 0 20 20"
                  >
                    <path d="M10 .5a9.5 9.5 0 1 0 9.5 9.5A9.51 9.51 0 0 0 10 .5Zm3.707 8.207-4 4a1 1 0 0 1-1.414 0l-2-2a1 1 0 0 1 1.414-1.414L9 10.586l3.293-3.293a1 1 0 0 1 1.414 1.414Z" />
                  </svg>
                  <Link
                    className="font-medium rounded-lg text-sm py-2 text-center flex items-center"
                    to="/perft/1/9/results"
                  >
                    perft(9)
                  </Link>
                </li>
              </ul>
            </div>
            </div>

          </div>
          <GlobalPerformanceChart/>
          <GlobalLeaderboard/>
          <PerftResults />
        </div>
      </div>
    </>
  );
};

export default Home;
