// Create-station page: add and remove feed URL rows.
(function () {
    var container = document.getElementById('feedsContainer');
    var addBtn = document.getElementById('addFeedBtn');

    if (!addBtn || !container) return;

    addBtn.addEventListener('click', function () {
        var div = document.createElement('div');
        div.className = 'feed-item';
        div.innerHTML = `
            <input type="url" name="Feeds" class="form-control" placeholder="https://..." required aria-label="Feed URL" />
            <button type="button" class="btn btn-ghost remove-feed">Remove</button>
        `;
        container.appendChild(div);
    });

    container.addEventListener('click', function (e) {
        if (e.target && e.target.classList.contains('remove-feed')) {
            var items = container.querySelectorAll('.feed-item');
            if (items.length > 1) {
                e.target.closest('.feed-item').remove();
            } else {
                // Just clear it if it's the last one
                e.target.previousElementSibling.value = '';
            }
        }
    });
})();
