"""One loaded model per engine and a bounded queue. Cancelled calls retain the slot."""
import asyncio
from functools import partial

import anyio


class EngineSlot:
    def __init__(self, factory):
        self.factory = factory
        self.engine = None
        self.lock = asyncio.Lock()

    async def run(self, data: bytes):
        await asyncio.wait_for(self.lock.acquire(), timeout=1)
        try:
            work = asyncio.create_task(anyio.to_thread.run_sync(partial(self._recognize, data)))
        except BaseException:
            self.lock.release()
            raise
        # Shield the independent worker task even against raw asyncio cancellation.
        # The completion callback owns the lock, not the cancelled HTTP waiter.
        work.add_done_callback(self._release)
        return await asyncio.shield(work)

    def _release(self, work):
        self.lock.release()
        if not work.cancelled():
            work.exception()  # Retrieve a failure even if its HTTP waiter disconnected.

    def _recognize(self, data: bytes):
        if self.engine is None:
            self.engine = self.factory()
        return self.engine.recognize(data)
