// Palettes follow the "Eorzea Passport" Claude Design canvas (18 October 2026).
// Each edition fills the {{token}} slots of the SVG templates in svg/.
export const shared = {
  paper: "#D8C5A3",
  ink: "#19242E",
  // The paper's guilloche rings; the client turns them light on a dark paper (a recoloured colour).
  lines: "#000000",
};

export const editions = {
  eorzea: { cover: "#19242E", foil: "#B99662", accent: "#B99662", stampInk: "#2A3B52" },
  gridania: { cover: "#1E3A2A", foil: "#A9C28A", accent: "#5E8A4A", stampInk: "#2F5A3A" },
  uldah: { cover: "#5A1424", foil: "#D4A83C", accent: "#9C3A4C", stampInk: "#7A2232" },
  ishgard: { cover: "#16213A", foil: "#C3CAD6", accent: "#7F8FB0", stampInk: "#2B3F6B" },
  limsa: { cover: "#2B5568", foil: "#DCE6E8", accent: "#B58A3C", stampInk: "#1F4A60" },
  garlemald: { cover: "#FFFFFF", foil: "#141414", accent: "#C0272D", stampInk: "#8E1C22" },
};

// The edition shown first in review sheets. The client embeds every edition; its PassportEdition.cs keeps the same colours.
export const runtimeEdition = "gridania";
