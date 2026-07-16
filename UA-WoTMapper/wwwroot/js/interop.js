// Triggers a client-side download of a text file (used to save the mapped Thing Model).
window.downloadText = function (fileName, text) {
    const blob = new Blob([text], { type: "application/ld+json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
};
