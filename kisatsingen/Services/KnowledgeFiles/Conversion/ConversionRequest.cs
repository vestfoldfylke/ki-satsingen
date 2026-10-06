namespace kisatsingen.Services.KnowledgeFiles.Conversion;

// MaxEstimatedTokens is for stopping early, which only a converter can judge:
// it should refuse as soon as its output passes the cap, rather than build all
// of it first. That matters most for formats whose output can dwarf the file
// (compressed PDF streams, zipped Office documents). A check on the byte count
// alone only makes sense where bytes predict text length, as for text files.
//
// The processor checks every result against the cap afterwards whatever the
// converter did, so this is about work saved, not the limit being enforced.
public sealed record ConversionRequest(string FileName, string ContentType, Stream Content, int MaxEstimatedTokens);
