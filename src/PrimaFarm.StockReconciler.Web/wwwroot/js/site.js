// Kept in a file (no inline script) so the Content-Security-Policy can forbid inline JavaScript.
(function () {
    const notesEl = document.getElementById("location-notes");
    const radios = document.querySelectorAll('input[name="locationId"]');

    function updateNotes() {
        if (!notesEl) return;
        const checked = document.querySelector('input[name="locationId"]:checked');
        notesEl.textContent = checked ? (checked.dataset.notes || "") : "Select an enabled location to continue.";
    }

    radios.forEach((el) => el.addEventListener("change", updateNotes));
    updateNotes();

    const form = document.getElementById("recon-form");
    const btn = document.getElementById("run-btn");
    if (form && btn) {
        form.addEventListener("submit", function () {
            btn.disabled = true;
            btn.textContent = "Comparing…";
        });
    }
})();
