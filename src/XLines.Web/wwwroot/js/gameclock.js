// The game clock, run in the browser between the server's readings (spec 2026-10-24 §6.4).
//
// The feed reads the clock every fifteen seconds. Left alone the title would show 4:12 for
// fifteen seconds and then jump to 3:57; ticking it on the server would send a render across the
// circuit every second for every open gamecast, which is the pattern windowing.js exists to
// avoid for pointer moves. So the server hands the element its reading and whether the clock is
// thought to be running, and this file counts the text down a second at a time. Every server
// render calls startClock again with a fresh reading, which corrects whatever the count drifted.
//
// ESPN does not say whether the clock is running. The window decides (GamecastPanel.ClockRun: a
// live game, a period not over, and the last two readings showing the clock moving with the wall
// clock) and passes `running`; `limit` is how many seconds the count may run before it stops on
// its own — what is left of one poll and a little slack past the reading (about eighteen
// seconds), never past 0:00. A stoppage the feed has not read yet costs at most that, and a feed
// that goes quiet leaves a clock that stops rather than one that runs on a guess. The rail chip
// keeps the server's reading, so the two may differ by at most that bound until the next one.

const clocks = new WeakMap();

export function startClock(el, seconds, running, limit) {
    if (!el) {
        return;
    }

    stopClock(el);

    let left = Math.max(0, Math.floor(seconds || 0));
    let budget = typeof limit === 'number' ? Math.max(0, Math.floor(limit)) : Infinity;

    // The text node Blazor rendered, written in place: replacing it would leave Blazor holding a
    // detached node, and its next update would land on nothing.
    const text = textNode(el);
    text.nodeValue = format(left);

    if (!running || left === 0 || budget === 0) {
        return;
    }

    const timer = setInterval(() => {
        // The window closed, or Blazor replaced the tag, without a stopClock: nothing to count on.
        if (!el.isConnected) {
            stopClock(el);
            return;
        }

        left -= 1;
        budget -= 1;
        text.nodeValue = format(Math.max(0, left));

        if (left <= 0 || budget <= 0) {
            stopClock(el);
        }
    }, 1000);

    clocks.set(el, timer);
}

export function stopClock(el) {
    if (!el) {
        return;
    }

    const timer = clocks.get(el);
    if (timer !== undefined) {
        clearInterval(timer);
        clocks.delete(el);
    }
}

function format(total) {
    const minutes = Math.floor(total / 60);
    const seconds = total % 60;
    return `${minutes}:${seconds < 10 ? '0' : ''}${seconds}`;
}

function textNode(el) {
    for (const node of el.childNodes) {
        if (node.nodeType === Node.TEXT_NODE) {
            return node;
        }
    }
    return el.appendChild(document.createTextNode(''));
}
