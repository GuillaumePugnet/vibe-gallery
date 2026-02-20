let observer = null;
let dotNetRef = null;

export function observeSentinel(sentinel, dotNetHelper) {
    dispose();

    dotNetRef = dotNetHelper;
    observer = new IntersectionObserver(entries => {
        if (entries[0].isIntersecting) {
            dotNetHelper.invokeMethodAsync('LoadMoreItems');
        }
    }, { rootMargin: '400px' });

    observer.observe(sentinel);
}

export function dispose() {
    if (observer) {
        observer.disconnect();
        observer = null;
    }
    dotNetRef = null;
}
