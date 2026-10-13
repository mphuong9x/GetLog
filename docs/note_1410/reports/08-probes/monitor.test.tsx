import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import TestMonitor from '../../../../MProjectFrontend/src/pages/TestMonitor/TestMonitor';

const mocks = vi.hoisted(() => ({
  t: (key: string) => key,
  api: { getThresholds: vi.fn(), getSummary: vi.fn(), getByStation: vi.fn(), getByComputer: vi.fn(), getErrorPareto: vi.fn(), search: vi.fn() },
}));
vi.mock('../../../../MProjectFrontend/src/api/testResults', () => ({ testResultsApi: mocks.api }));
vi.mock('react-i18next', () => ({ useTranslation: () => ({ t: mocks.t }) }));
vi.mock('antd', () => ({
  ConfigProvider: ({ children }: any) => children,
  Skeleton: () => <div>loading</div>, DatePicker: () => null,
  Select: ({ value, onChange, options }: any) => <select value={value || ''} onChange={e => onChange(e.target.value)}>{options.map((o: any) => <option key={o.value} value={o.value}>{o.label}</option>)}</select>,
  Input: { Search: () => null }, message: { error: vi.fn() },
}));
beforeEach(() => {
  vi.useFakeTimers();
  vi.clearAllMocks();
  mocks.api.getThresholds.mockResolvedValue(undefined);
  mocks.api.getSummary.mockResolvedValue({ total: 123, failRatePercent: 0, byDay: [] });
  mocks.api.getByStation.mockResolvedValue([]);
  mocks.api.getByComputer.mockResolvedValue([]);
  mocks.api.getErrorPareto.mockResolvedValue([]);
  mocks.api.search.mockResolvedValue({ items: [] });
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

it('reproduces no refresh after ten minutes on the mounted current-shift monitor', async () => {
  await act(async () => { render(<TestMonitor />); });
  expect(mocks.api.getSummary).toHaveBeenCalledTimes(1);
  await act(async () => { await vi.advanceTimersByTimeAsync(600000); });
  expect(mocks.api.getSummary).toHaveBeenCalledTimes(1);
});

it('reproduces old Sfis response overwriting the selected Golden response', async () => {
  let resolveOld!: (v: any) => void;
  mocks.api.getSummary.mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }));
  mocks.api.getSummary.mockResolvedValue({ total: 222, failRatePercent: 0, byDay: [] });
  await act(async () => { render(<TestMonitor />); });
  const source = screen.getAllByRole('combobox')[2];
  await act(async () => { fireEvent.change(source, { target: { value: 'Golden' } }); });
  expect(screen.getByText('222')).toBeTruthy();
  await act(async () => { resolveOld({ total: 111, failRatePercent: 0, byDay: [] }); });
  expect((source as HTMLSelectElement).value).toBe('Golden');
  expect(screen.getByText('111')).toBeTruthy();
});
