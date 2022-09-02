# Sample Builder

I wanted to better understand how [GitVersion](https://gitversion.net/) would behave for my team's workflow, so I created this project.
The code creates a new, scratch git repository, then runs a "script" against that repo, gathering GitVersion information as it goes.
It then creates a sequence diagram, illustrating the behavior.

# Set Up

For the code to work, you must have the following installed and in your path:

* `git` - `brew install git`
* `gitversion` CLI - `brew install gitversion`
* `plantuml` - `brew install plantuml`

