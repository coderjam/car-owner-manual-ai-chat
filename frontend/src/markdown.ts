import MarkdownIt from 'markdown-it';

const markdown = new MarkdownIt({
  breaks: true,
  html: false,
  linkify: true,
  typographer: false
});

const defaultLinkOpen = markdown.renderer.rules.link_open;

markdown.renderer.rules.link_open = (tokens, index, options, env, self) => {
  tokens[index].attrSet('target', '_blank');
  tokens[index].attrSet('rel', 'noopener noreferrer');

  return defaultLinkOpen
    ? defaultLinkOpen(tokens, index, options, env, self)
    : self.renderToken(tokens, index, options);
};

const renderedAnswers = new Map<string, string>();

export function renderMarkdown(content: string): string {
  const cached = renderedAnswers.get(content);
  if (cached !== undefined) {
    return cached;
  }

  const rendered = markdown.render(content);
  renderedAnswers.set(content, rendered);
  return rendered;
}
