import { lineDiff } from './line-diff';

describe('lineDiff', () => {
  it('identical texts are all same', () => {
    // Act
    const diff = lineDiff('a\nb', 'a\nb');

    // Assert
    expect(diff).toEqual([{ kind: 'same', text: 'a' }, { kind: 'same', text: 'b' }]);
  });

  it('a create is all add', () => {
    // Act
    const diff = lineDiff('', 'a\nb');

    // Assert
    expect(diff).toEqual([{ kind: 'add', text: 'a' }, { kind: 'add', text: 'b' }]);
  });

  it('a changed middle line is del then add', () => {
    // Act
    const diff = lineDiff('a\nb\nc', 'a\nB\nc');

    // Assert
    expect(diff).toEqual([
      { kind: 'same', text: 'a' },
      { kind: 'del', text: 'b' },
      { kind: 'add', text: 'B' },
      { kind: 'same', text: 'c' },
    ]);
  });
});
