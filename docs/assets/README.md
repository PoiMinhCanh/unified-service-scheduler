# Documentation assets

Use `docs/assets/` for images, screenshots, diagrams, PDFs, and other reference
files that support ticket requirements or project documentation. Describe the
requirements and acceptance criteria in the ticket, and link to assets as
supporting context.

Project assets are added as needed. Check documentation links when removing assets.

When adding an asset:

- Use a descriptive filename, prefixed with the ticket number when applicable,
  such as `1-booking-flow.png` or `1-requirements.pdf`.
- Link it from the relevant ticket or document and explain what it shows or which
  requirement it supports. For a PDF, include the relevant page numbers.
- Add files only when needed; a ticket does not need an image or PDF to be valid.
- Exclude credentials, personal data, and unrelated material from other projects.

From a ticket in `docs/tickets/`, use relative Markdown links:

```markdown
![Booking flow](../assets/1-booking-flow.png)
[Requirement reference, pages 2-3](../assets/1-requirements.pdf)
```

These filenames are examples; the files do not exist yet.
