// Trigger a save-as dialog from a stream of bytes piped over the
// SignalR circuit. Used by Shot / Log / Run binary download buttons:
// the .NET side fetches the bytes via its (bearer-authed) HttpClient
// and hands the resulting stream to this function via the standard
// DotNetStreamReference plumbing.
//
// Pattern lifted straight from the Blazor docs' "Download files
// from a stream" sample — works under both InteractiveServer and
// Static SSR with Enhanced Navigation.

window.enkiDownloads = {
    fromStream: async (fileName, contentStreamReference) => {
        const arrayBuffer = await contentStreamReference.arrayBuffer();
        const blob   = new Blob([arrayBuffer], { type: "application/octet-stream" });
        const url    = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href     = url;
        anchor.download = fileName ?? "download.bin";
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
        URL.revokeObjectURL(url);
    },
};
