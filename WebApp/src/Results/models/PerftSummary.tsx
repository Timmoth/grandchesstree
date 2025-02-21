import { ChessData } from "./ChessData";

export interface PerftSummary {
  position_name: string;
  position_fen: string;
  position_description: string;
  results: ChessData[];
  unique_positions: number[];

}
