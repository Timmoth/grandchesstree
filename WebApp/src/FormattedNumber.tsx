import React from "react";
import { formatBigNumber } from "./Utils";

interface FormattedNumberProps {
  value: number;
  min: number;
  max: number;
}

const predefinedClasses = [
  "text-gray-300", "text-gray-400", "text-gray-500", // Common
  "text-green-400", "text-green-500", "text-green-600", // Uncommon
  "text-blue-400", "text-blue-500", "text-blue-600", // Rare
  "text-purple-400", "text-purple-500", "text-purple-600", // Epic
  "text-orange-400", "text-orange-500", "text-orange-600"  // Legendary
];

const interpolateColor = (num: number, min: number, max: number): string => {
  if (num <= min) return predefinedClasses[0];
  if (num >= max) return predefinedClasses[predefinedClasses.length - 1];
  
  const logMin = Math.log10(min);
  const logMax = Math.log10(max);
  const logValue = Math.log10(num);
  const scale = (logValue - logMin) / (logMax - logMin);
  
  const index = Math.floor(scale * (predefinedClasses.length - 1));
  return predefinedClasses[index];
};

const FormattedNumber: React.FC<FormattedNumberProps> = ({ value, min, max }) => {
  return <span className={interpolateColor(value, min, max)}>{formatBigNumber(value)}</span>;
};

export default FormattedNumber;